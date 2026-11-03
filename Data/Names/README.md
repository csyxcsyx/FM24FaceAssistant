# Offline name data

Names are processed locally, solely to choose fictional artwork. These records do not verify a person's gender. No player or staff names are sent to an online service.

## Jörg Michael name dictionary

`nam_dict.txt` is the unmodified UTF-8 dictionary by Jörg Michael, version 1.2 (2008-11-30), containing 47,780 name/country/gender records. Multiple records can refer to the same name. It covers 54 named country/region categories and an additional other-countries category.

Source: https://github.com/lead-ratings/gender-guesser/blob/master/gender_guesser/data/nam_dict.txt

Copyright 2007–2008 Jörg Michael. GNU Free Documentation License 1.2 or later, with no Invariant Sections, no Front-Cover Texts and no Back-Cover Texts. Full license: `GNU_DOC.TXT`. The dictionary retains its original notices. No gender-guesser Python code is bundled or ported; the application has its own C# reader and conservative decision rules.

SHA256: BFE79B1F3533D188333C36D7B0722868B873B2D366BB15BED3ED9A4F85DB3C8C

## Onomaverse v2026.10

Names data from Onomaverse (https://onomaverse.com/datasets), licensed CC BY 4.0.

`gender.csv` is an unmodified download of `name-gender-inference.csv`, containing 18,204 global given-name probability rows. These are coverage-limited estimates, not census counts or proof about an individual.

Source: https://github.com/onomaverse/datasets/releases/tag/v2026.10

License: `LICENSE.txt`. Upstream notices: `NOTICE` (including Apache 2.0 text).

gender.csv SHA256: 94AD7052AE1B7229F5DCEDBC535D53D5ABEA93E0D0D77515E266B525E5D95DD7

## Application rules

Players use male artwork. Explicit staff data is preferred; otherwise, country-specific dictionary evidence is checked before global single-gender entries and high-confidence supplementary probabilities. An additional small country-name rule table handles common conflicting usages. For female dictionary recommendations, supplementary global statistics below 90% female are treated as ambiguous, unless an explicit local rule applies. This is a conservative artwork policy, not a validated probability of an individual's gender. Ambiguous or unknown cases default to male. Common titles, diacritics, compound names, comma-separated surname-first names and selected East Asian surname-first formats are supported. Limited explicit language markers supplement names absent from the dictionaries. A generic name-ending rule is deliberately not used.

Raw `Person.Gender` is preserved. Computed artwork gender is not serialized as verified game data. Existing assignments and locks are preserved; manually browsing all staff artwork permits correction.
