using System.Collections.Frozen;

namespace FluxEncoder.Core.Media;

/// <summary>
/// Maps ISO 639-1 two-letter codes and ISO 639-2/B bibliographic codes to ISO 639-2/T, so that
/// <c>en</c>/<c>eng</c> and <c>fre</c>/<c>fra</c> compare equal.
/// </summary>
/// <remarks>
/// The table is embedded rather than read from <see cref="System.Globalization.CultureInfo"/> because the
/// Native AOT build may run with invariant globalization. It covers the 639-1 codes ICU knows plus a few
/// common gaps (Latin, Tagalog); a code not listed compares only to itself.
/// </remarks>
internal static class IsoLanguages
{
    private const string Alpha1ToAlpha3 =
        "af:afr ak:aka am:amh ar:ara as:asm az:aze be:bel bg:bul bm:bam bn:ben bo:bod br:bre bs:bos ca:cat " +
        "ce:che co:cos cs:ces cv:chv cy:cym da:dan de:deu dz:dzo ee:ewe el:ell en:eng eo:epo es:spa et:est " +
        "eu:eus fa:fas ff:ful fi:fin fo:fao fr:fra fy:fry ga:gle gd:gla gl:glg gu:guj gv:glv ha:hau he:heb " +
        "hi:hin hr:hrv ht:hat hu:hun hy:hye ia:ina id:ind ig:ibo ii:iii is:isl it:ita ja:jpn jv:jav ka:kat " +
        "ki:kik kk:kaz kl:kal km:khm kn:kan ko:kor ks:kas ku:kur kw:cor ky:kir la:lat lb:ltz lg:lug ln:lin " +
        "lo:lao lt:lit lu:lub lv:lav mg:mlg mi:mri mk:mkd ml:mal mn:mon mr:mar ms:msa mt:mlt my:mya nb:nob " +
        "nd:nde ne:nep nl:nld nn:nno no:nor ny:nya om:orm or:ori os:oss pa:pan pl:pol ps:pus pt:por qu:que " +
        "rm:roh rn:run ro:ron ru:rus rw:kin sa:san sc:srd sd:snd se:sme sg:sag si:sin sk:slk sl:slv sm:smo " +
        "sn:sna so:som sq:sqi sr:srp st:sot su:sun sv:swe sw:swa ta:tam te:tel tg:tgk th:tha ti:tir tk:tuk " +
        "tl:tgl tn:tsn to:ton tr:tur tt:tat ug:uig uk:ukr ur:urd uz:uzb vi:vie wo:wol xh:xho yi:yid yo:yor " +
        "zh:zho zu:zul";

    private const string BibliographicToTerminology =
        "alb:sqi arm:hye baq:eus bur:mya chi:zho cze:ces dut:nld fre:fra geo:kat ger:deu " +
        "gre:ell ice:isl mac:mkd mao:mri may:msa per:fas rum:ron slo:slk tib:bod wel:cym";

    private static readonly FrozenDictionary<string, string> ToTerminology = Build();

    /// <summary>
    /// The ISO 639-2/T form of a lower-case primary subtag, or the subtag itself when it has no mapping.
    /// </summary>
    public static string Canonicalize(string primarySubtag) =>
        ToTerminology.TryGetValue(primarySubtag, out var code) ? code : primarySubtag;

    private static FrozenDictionary<string, string> Build() =>
        $"{Alpha1ToAlpha3} {BibliographicToTerminology}"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(':'))
            .ToFrozenDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}
