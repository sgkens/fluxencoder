#!/usr/bin/env node

/**
 * Projects repository-owned metadata into 11ty's data cascade (`site/_data/generated.json`)
 * and copies the authoritative JSON Schema into `site/schema/fluxfile-v1.schema.json`.
 *
 * Sources of truth:
 * - Directory.Build.props (TargetFramework, AnalysisLevel, compiler policies)
 * - Directory.Packages.props (Centrally pinned NuGet package versions)
 * - global.json (Pinned .NET SDK version and test runner)
 * - src/FluxEncoder.Core/Results/ExitCode.cs (Public process exit code contract)
 * - docs/schema/fluxfile-v1.schema.json (Fluxfile v1 JSON Schema)
 */

import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = resolve(__dirname, '..');

function extractXmlTag(xml, tagName) {
  const match = xml.match(new RegExp(`<${tagName}>([^<]+)</${tagName}>`));
  return match ? match[1].trim() : null;
}

function parsePackagesProps(xml) {
  const packages = [];
  const regex = /<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"\s*\/>/g;
  let match;
  while ((match = regex.exec(xml)) !== null) {
    packages.push({
      name: match[1],
      version: match[2],
    });
  }
  return packages;
}

function parseExitCodes(csharpSource) {
  const codes = [];
  const lines = csharpSource.split(/\r?\n/);
  let summaryLines = [];

  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (line.startsWith('///')) {
      const text = line
        .replace(/^\/\/\/\s*/, '')
        .replace(/<\/?summary>/g, '')
        .replace(/<see cref="([^"]+)"\s*\/>/g, '$1')
        .replace(/<c>([^<]+)<\/c>/g, '`$1`')
        .trim();
      if (text && !line.includes('<remarks>') && !line.includes('</remarks>')) {
        summaryLines.push(text);
      }
      continue;
    }

    const enumMatch = line.match(/^([A-Za-z0-9_]+)\s*=\s*(\d+)\s*,?$/);
    if (enumMatch) {
      const name = enumMatch[1];
      const code = Number(enumMatch[2]);
      const description =
        summaryLines.join(' ').trim() ||
        (name === 'Success'
          ? 'The command completed without errors.'
          : name === 'Timeout'
            ? 'The operation exceeded the configured run-time limit or stalled.'
            : name === 'Cancelled'
              ? 'Interrupted by SIGINT (Ctrl+C) or SIGTERM; FFmpeg was finalized gracefully and partial output was cleaned up.'
              : '');
      codes.push({ name, code, description });
      summaryLines = [];
    } else if (line.startsWith('public enum')) {
      summaryLines = [];
    }
  }

  return codes;
}

function parseSchemaOptions(schema) {
  const topLevel = Object.entries(schema.properties ?? {}).map(([key, prop]) => ({
    key,
    required: Array.isArray(schema.required) && schema.required.includes(key),
    type: prop.const !== undefined ? `const: ${prop.const}` : prop.type ?? (prop.$ref ? prop.$ref.replace('#/$defs/', '') : 'object'),
    description: prop.description ?? (key === '$schema' ? 'JSON Schema URI for editor validation and completion.' : 'Format version (must be 1).'),
  }));

  const jobProps = Object.entries(schema.$defs?.job?.properties ?? {}).map(([key, prop]) => ({
    key,
    required: Array.isArray(schema.$defs?.job?.required) && schema.$defs.job.required.includes(key),
    type: prop.type ?? (prop.$ref ? prop.$ref.replace('#/$defs/', '') : 'string'),
    description: prop.description ?? 'Free-text human description of the job.',
  }));

  const optionProps = Object.entries(schema.$defs?.optionProperties?.properties ?? {}).map(([key, prop]) => {
    let valueType = 'string';
    if (Array.isArray(prop.enum)) {
      valueType = prop.enum.map((v) => `"${v}"`).join(' | ');
    } else if (prop.$ref) {
      valueType = prop.$ref.replace('#/$defs/', '');
    } else if (Array.isArray(prop.type)) {
      valueType = prop.type.join(' | ');
    } else if (prop.type) {
      valueType = prop.type;
    }

    return {
      key,
      cliFlag: `--${key}`,
      type: valueType,
      description: prop.description ?? (prop.$ref === '#/$defs/flag' ? 'Boolean flag (true or false).' : 'Encode option parameter.'),
    };
  });

  return { topLevel, jobProps, optionProps };
}

async function main() {
  const [buildPropsXml, packagesPropsXml, globalJsonRaw, exitCodeCs, schemaJsonRaw] = await Promise.all([
    readFile(resolve(rootDir, 'Directory.Build.props'), 'utf8'),
    readFile(resolve(rootDir, 'Directory.Packages.props'), 'utf8'),
    readFile(resolve(rootDir, 'global.json'), 'utf8'),
    readFile(resolve(rootDir, 'src/FluxEncoder.Core/Results/ExitCode.cs'), 'utf8'),
    readFile(resolve(rootDir, 'docs/schema/fluxfile-v1.schema.json'), 'utf8'),
  ]);

  const globalJson = JSON.parse(globalJsonRaw);
  const schemaJson = JSON.parse(schemaJsonRaw);

  const generated = {
    project: {
      name: 'FluxEncoder',
      tagline: 'Deterministic CLI workspace, planner, and telemetry layer for FFmpeg',
      repositoryUrl: 'https://gitlab.com/sgkens/fluxencoder',
      targetFramework: extractXmlTag(buildPropsXml, 'TargetFramework') ?? 'net10.0',
      analysisLevel: extractXmlTag(buildPropsXml, 'AnalysisLevel') ?? 'latest',
      sdkVersion: globalJson.sdk?.version ?? '10.0.100',
      sdkRollForward: globalJson.sdk?.rollForward ?? 'latestFeature',
      testRunner: globalJson.test?.runner ?? 'Microsoft.Testing.Platform',
      ciImage: 'sgkens/phellams-automator:10.0.400',
    },
    packages: parsePackagesProps(packagesPropsXml),
    exitCodes: parseExitCodes(exitCodeCs),
    fluxfileSchema: {
      id: schemaJson.$id,
      draft: schemaJson.$schema,
      title: schemaJson.title,
      description: schemaJson.description,
      ...parseSchemaOptions(schemaJson),
    },
  };

  const dataOutPath = resolve(rootDir, 'site/_data/generated.json');
  const schemaOutPath = resolve(rootDir, 'site/schema/fluxfile-v1.schema.json');

  await mkdir(dirname(dataOutPath), { recursive: true });
  await mkdir(dirname(schemaOutPath), { recursive: true });

  await writeFile(dataOutPath, `${JSON.stringify(generated, null, 2)}\n`, 'utf8');
  await copyFile(resolve(rootDir, 'docs/schema/fluxfile-v1.schema.json'), schemaOutPath);

  console.log(`Generated site data -> site/_data/generated.json (${generated.exitCodes.length} exit codes, ${generated.fluxfileSchema.optionProps.length} schema options)`);
}

await main();
