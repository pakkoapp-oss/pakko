# Microsoft Store listing texts

The text of Pakko's Store listing (Partner Center, "Store listings"), one file per language. Until
2026-10-05 this text lived only in Partner Center; it is kept here so that a change is reviewed,
diffed and pasted from a file, not from a chat window or a terminal.

The files hold the text **for the v1.8.0 submission**: the PAR2 feature line and "What's new" were
added on 2026-10-10, the rest is the v1.7.0 text. "What's new" covers v1.7.2 and v1.8.0 together,
because the Store served 1.7.1 when it was written. What is live in the Store can be read
without signing in:
`https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=9P5MW010D8PR&market=CZ&languages=cs-CZ`
(`ProductDescription`, `ShortDescription`, `SearchTitles`, and `Features` under the SKU).

## File format

`<locale>.txt`, UTF-8, named like the folders under `src/Archiver.App/Strings/`. Five sections,
each pasted into the Partner Center field of the same name:

| Section | Partner Center limit | In the file |
|---|---|---|
| `[Description]` | 10,000 characters | four paragraphs |
| `[Short description]` | 1,000; some views show only the first 270 | one paragraph, at most 270 characters |
| `[Product features]` | 20 items, 200 characters each | 13 lines, one feature per line |
| `[Search terms]` | 7 terms | 7 lines, each at most 30 characters, 21 words in all |
| `[What's new in this version]` | 1,500 characters | 5 lines, each starting with a bullet |

The first four limits are from Microsoft's "Add and edit Store listing info" page; the limits on
search terms are the ones the files were checked against, not read from that page.

Two fields are the same in every language and are not in the files: the copyright line is
`Copyright © 2026 Pakko Contributors` (the notice in `LICENSE`), and screenshot captions are left
empty, because Partner Center does not keep the screenshots in the same order in every language.

`ar-SA`, `he-IL` and `ur-PK` have a left-to-right mark (U+200E) before `.NET` where it starts a
right-to-left run, so that it does not display as `NET.`.

## Getting the text into Partner Center

`scripts/Fill-StoreListing.py` fills a listing CSV exported from Partner Center from these files
(`scripts/README.md` has the options); the CSV is then imported by hand.

## Rules for the text

- The same facts in every language; `en-US.txt` is the source. Check a claim against the code or
  `CHANGELOG.md` before adding it.
- Words for actions are the app's words: `tests/Archiver.Messages.Tests/Glossary.tsv`.
- The menu items named in feature 5 are the labels in `src/Archiver.ShellExtension/Localization.cpp`
  (Open, Extract here, Compress, Test archive, Scan for threats, Hash). "Add to archive" is not
  named: it is the fallback title of the item that normally reads `Add to "name.zip"`.
- A window or dialog named in the text carries the title it has in that locale's
  `Resources.resw` (the conflict dialog is `ConflictDialogTitle`).
- Search terms keep the words people type into a search box ("rar extractor", "розпакувати
  rar"), even where the app's own word for the action is another.
- File Explorer has Microsoft's name for that language (support.microsoft.com, "File Explorer in
  Windows"): bg Файлов мениджър, ro Explorer, uk Файловий провідник, sk Prieskumník,
  sl Raziskovalec, hr Eksplorer za datoteke, sr Istraživač datoteka; et, hi, id, th, vi and sw
  keep "File Explorer".
- No comparison with what Explorer does or does not do. The old text said Explorer does not pass
  the Mark-of-the-Web on. Tested 2026-10-05 on Windows 11 build 26300: a ZIP marked `ZoneId=3`,
  extracted by Explorer's own ZIP folder (`Shell.Application`, `CopyHere`), gave both extracted
  files `ZoneId=3`. Explorer's handling of 7z, RAR and tar was not tested.

## Review of the published listing, 2026-10-05

Compared: the 32 files the user exported from Partner Center and the public catalog (same text,
except that the Spanish export lost its colons and slashes on the way; the Store has them).

In every language:

| What | Was | Now |
|---|---|---|
| Features of v1.5-v1.7 | not mentioned: password-protected ZIP, Scan for threats, Hash, the `pakko` command in the package, 37 languages; the menu list was "Extract, Add to archive, Test archive" | in the description, short description and features |
| Mark-of-the-Web | "that Explorer itself doesn't propagate" | "keeps the tag of the archive it came from" |
| Product features | en, uk: 9 short lines; others: the 9 "key features" bullets; en, uk repeated them inside the description | one list of 12 in every language, none inside the description |
| Short description | empty in en, cs, es, it, uk; elsewhere a 9-line list | one paragraph |
| What's new | empty | 5 lines for v1.7.0 |
| hr-HR, sl-SI, sr-Latn-RS, ur-PK, vi-VN | no listing; the Store shows English | written; the languages have to be added in Partner Center |

By language (defects in the published text):

| Locale | Field | Published | Fixed to |
|---|---|---|---|
| ar-SA | all | every line stored back to front, letter by letter (`)pakko.exe(`, `Zip-7`); unreadable | written anew |
| he-IL | all | the same | written anew |
| cs-CZ | description | "Žádná telemesíťové požadavky" (words lost) | "Žádná telemetrie, žádná analytika, žádné síťové požadavky" |
| da-DK | description | "Ingen teleetværksanmodninger" | "Ingen telemetri, ingen brugsanalyse, ingen netværksanmodninger" |
| el-GR | description | "να δει ακριβώ — ... δημόσιαδιαθέσιμος" | sentence restored |
| el-GR | all | αποσυμπίεση, αρχείο (also "file"), "Πρόγραμμα περιήγησης αρχείων" | app terms: Εξαγωγή, αρχείο συμπίεσης |
| lt-LT | description | "Jokios telemetri tinklo užklausų." | sentence restored |
| lt-LT | features | "Tikrinti archyvą" (the app's word for Scan) | "Testuoti archyvą" |
| nb-NO | description | "hva som ele kildekoden ... påGitHub" | sentence restored |
| pl-PL | description | "Apache 2.0.tyki, ... jakiegokolwiekrodzaju" | sentence restored |
| ro-RO | description | "să vadă exact cul cod sursă ... sublicența" | sentence restored |
| ro-RO | all | "Explorer-ul de fișiere" | "Explorer" |
| sv-SE | description | "Ingen telemetri, örfrågningar av något slag." | sentence restored |
| sv-SE | all | "packa upp" | app term "extrahera" |
| th-TH | description | last sentence cut off ("ไม่มีการร้องขอเค") | sentence restored |
| fi-FI | description | "puretti tiedosto" | "purettu tiedosto" |
| es-ES | search terms | "Claro" (a stray word) | removed |
| es-ES | all | "archivo" for both file and archive; "Comprobar archivo" | "archivo comprimido"; "Probar archivo comprimido" |
| bg-BG | all | "Файловия explorer"; "Естествено контекстно меню" | "Файловия мениджър"; "Контекстно меню" |
| et-EE | all | "Failihaldur"; "pakkimis-API" | "File Explorer"; "tihendus-API" |
| uk-UA | all | "Провідник файлів"; розпакувати, "Перевірити архів" (the app's word for Scan) | "Файловий провідник"; видобути, "Тестувати архів" |
| it-IT | features | "Verifica archivio" (the app's word for Scan) | "Testa archivio" |
| it-IT, da-DK, de-DE | search terms | "gz", "bz2" as terms of their own | replaced |
| lv-LV | all | "izpakot" | app term "izvilkt" |
| tr-TR | description | "Gezgini'nin kendisinin"; "sanal alan" for sandbox; "çıkar" | sentence removed; "korumalı alan"; app term "ayıkla" |
| hi-IN | all | आर्काइव, एक्सट्रैक्ट, कंप्रेशन | app terms संग्रह, निकालें, संपीड़न |
| hi-IN | search terms | "Pakko zip आरकाइवर" (misspelt duplicate) | removed |
| sw-KE | all | "fungua" (open) for extract; "faili iliyogandamizwa" | app terms "toa", "kumbukumbu" |
| ja-JP | search terms | "Pakko zip アーカイバー" twice | one |
| ko-KR | all | 아카이버, 텔레메트리 | 압축 프로그램, 원격 분석 |
| zh-Hans | all | 归档工具; half-width commas and brackets | 压缩软件; full-width punctuation |
| sk-SK | all | "Prieskumník súborov" | "Prieskumník" |
| sv-SE, sw-KE | search terms | 5 terms | 7 |

Not checked by a native speaker (there is none for 36 of the languages); the terms were checked by
script against the glossary and the menu table.
