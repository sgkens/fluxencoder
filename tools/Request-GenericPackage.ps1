function Request-GenericPackage {
    param (
        [parameter(Mandatory=$true)]
        [string]$ProjectId,
        [parameter(Mandatory=$false)]
        [string]$PackageName,
        [string]$PackageVersion,
        [string]$NameSpace,
        [string]$projectName,
        [string]$ApiUrl,
        [string]$ApiKey,
        [switch]$ci
    )

     # Default to GitLab's public API if no ApiUrl is provided

    if(!$ApiUrl) { $ApiUrl = "https://gitlab.com/api/v4" }
    if(!$projectName){ $projectName = $ENV:CI_PROJECT_NAME }
    if(!$NameSpace){ $NameSpace = $ENV:CI_PROJECT_NAMESPACE }

        # Construct the API URL for fetching the package details
    $url = "$ApiUrl/projects/$ProjectId/packages?package_name=$PackageName&package_version=$PackageVersion&package_Type=generic"

    Write-Host "Requesting package info from URL: $url"

    $headers = @{ "Content-Type" = "application/json" }
    if ($ApiKey) {
        if ($ci) {
            $headers["JOB-TOKEN"] = $ApiKey
        } else {
            $headers["PRIVATE-TOKEN"] = $ApiKey
        }
    }
     # Make the API request to get package details.

    try {
        # Make the API request to get package details
        $response = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers

        if ($response -and $response.Count -gt 0) {
            $generic_package_id = $response[0].id
            $package_files = Invoke-RestMethod -Uri "$ApiUrl/projects/$ProjectId/packages/$generic_package_id/package_files?per_page=100" -Headers $Headers

            $generic_package_files_reponse = @()

            foreach($package in $package_files){
                #$download_url = "$ApiUrl/projects/$ProjectId/packages/generic/$generic_package_id/package_files/$($package.id)/download"
                $download_url = "https://gitlab.com/$NameSpace/$projectName/-/package_files/$($package.id)/download"
                # NOTE: THE API LINK IS NOT CORRECT and downloads the file with the name download
                write-host "Generating Generic Package File: $($package.package_id) Metadata for: $NameSpace/$projectName/-/package_files/$($package.id)"
                $generic_package_files_reponse += [pscustomobject]@{
                    package_id   = $package.package_id
                    id           = $package.id
                    file_name    = $package.file_name
                    file_sha256  = $package.file_sha256
                    size         = $package.size
                    created_at   = $package.created_at
                    web_path     = $package.web_path
                    download_url = $download_url
                }
            }
            return $generic_package_files_reponse
        } else {
            Write-Error "No package found with name '$PackageName' and version '$PackageVersion'."
            return $null
        }
    } catch {
        Write-Error "Failed to retrieve package information: $_"
        return $null
    }
}

# $cmdlet_config = @{
#     function = @(
#         'Request-GenericPackage'
#     )
#     alias = @()
# }

# Export-ModuleMember @cmdlet_config

# Example usage:
# Request-GenericPackage -ProjectId "72971048" -PackageType "generic" -PackageName "fastfsc" -PackageVersion "v0.4.0-prerelease" -ApiUrl "https://gitlab.com/api/v4" -ApiKey $ENV:GITLAB_API_KEY
