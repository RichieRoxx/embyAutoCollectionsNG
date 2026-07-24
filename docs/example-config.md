# Example configuration: collection rules

This is a set of example `CollectionRule` entries for the German TV recording
use case described in [`PLAN.md`](PLAN.md). Each rule matches a recording's
title (or, if `AlsoMatchFileName` is enabled, its file name) and adds it to
the named collection.

All example patterns use `(?i)` for case-insensitivity, which is redundant
with `CaseSensitive: false` but kept explicit in the pattern for readability
and portability (e.g. copy-pasting the pattern into another tool that doesn't
know about the `CaseSensitive` flag).

| Collection name | Match type | Pattern | Notes |
|---|---|---|---|
| Formel 1 | Regex | `(?i)\bformel\s*1\b` | Matches "Formel 1", "formel1", "FORMEL 1" etc. Word boundaries avoid matching inside longer words. |
| heute-show | Regex | `(?i)\bheute[- ]show\b` | Matches both the hyphenated ("heute-show") and space-separated ("heute show") spelling variants seen in EPG data. |
| ZDF Magazin Royale | Contains | `ZDF Magazin Royale` | Simple substring match; `CaseSensitive: false` so "zdf magazin royale" also matches. |
| ZDF Satire | Regex | `(?i)\b(heute[- ]show\|zdf magazin royale\|die anstalt)\b` | A broader "umbrella" collection grouping multiple satire shows into one. Overlaps intentionally with the two more specific rules above — a recording can belong to more than one collection. |
| Motorsport | Regex | `(?i)\b(formel\s*[1-3]\|motogp\|dtm)\b` | Groups Formula 1/2/3, MotoGP, and DTM recordings into one motorsport collection, in addition to the dedicated "Formel 1" collection. |

## Corresponding `PluginConfiguration` XML fragment

For reference, here is what these five rules look like once serialized as
part of `PluginConfiguration` (the `<Rules>` XML element is the array
representation `System.Xml.Serialization.XmlSerializer` produces for
`CollectionRule[]`):

```xml
<PluginConfiguration>
  <DeleteEmptyCollections>false</DeleteEmptyCollections>
  <Rules>
    <CollectionRule>
      <CollectionName>Formel 1</CollectionName>
      <MatchType>Regex</MatchType>
      <Pattern>(?i)\bformel\s*1\b</Pattern>
      <CaseSensitive>false</CaseSensitive>
      <Enabled>true</Enabled>
      <MatchOn>RawTitle</MatchOn>
      <AlsoMatchFileName>false</AlsoMatchFileName>
    </CollectionRule>
    <CollectionRule>
      <CollectionName>heute-show</CollectionName>
      <MatchType>Regex</MatchType>
      <Pattern>(?i)\bheute[- ]show\b</Pattern>
      <CaseSensitive>false</CaseSensitive>
      <Enabled>true</Enabled>
      <MatchOn>RawTitle</MatchOn>
      <AlsoMatchFileName>false</AlsoMatchFileName>
    </CollectionRule>
    <CollectionRule>
      <CollectionName>ZDF Magazin Royale</CollectionName>
      <MatchType>Contains</MatchType>
      <Pattern>ZDF Magazin Royale</Pattern>
      <CaseSensitive>false</CaseSensitive>
      <Enabled>true</Enabled>
      <MatchOn>RawTitle</MatchOn>
      <AlsoMatchFileName>false</AlsoMatchFileName>
    </CollectionRule>
    <CollectionRule>
      <CollectionName>ZDF Satire</CollectionName>
      <MatchType>Regex</MatchType>
      <Pattern>(?i)\b(heute[- ]show|zdf magazin royale|die anstalt)\b</Pattern>
      <CaseSensitive>false</CaseSensitive>
      <Enabled>true</Enabled>
      <MatchOn>RawTitle</MatchOn>
      <AlsoMatchFileName>false</AlsoMatchFileName>
    </CollectionRule>
    <CollectionRule>
      <CollectionName>Motorsport</CollectionName>
      <MatchType>Regex</MatchType>
      <Pattern>(?i)\b(formel\s*[1-3]|motogp|dtm)\b</Pattern>
      <CaseSensitive>false</CaseSensitive>
      <Enabled>true</Enabled>
      <MatchOn>RawTitle</MatchOn>
      <AlsoMatchFileName>false</AlsoMatchFileName>
    </CollectionRule>
  </Rules>
</PluginConfiguration>
```

Note: `LibraryFilter` and `ItemTypeFilter` are omitted above for brevity; with
their default empty-array value (meaning "no restriction"), `XmlSerializer`
renders them as an empty self-closing element, e.g. `<LibraryFilter />`.
