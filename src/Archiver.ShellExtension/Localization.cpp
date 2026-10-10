#include "pch.h"
#include "Localization.h"
#include <unordered_map>

namespace
{
    struct LocalizedStrings
    {
        const wchar_t* extractDialog;
        const wchar_t* extractHereFlat;
        const wchar_t* extractHereIntelligent;
        const wchar_t* extractFolderFallback;
        const wchar_t* extractFolderMultiFallback;
        const wchar_t* extractFolderNamedTemplate;
        const wchar_t* compressDialog;
        const wchar_t* archiveFallback;
        const wchar_t* archiveNamedTemplate;
        const wchar_t* testArchive;
        const wchar_t* scanArchive;
        const wchar_t* browseArchive;
        const wchar_t* hashSubmenu;
        const wchar_t* selectionNotOnDisk;
        const wchar_t* launchFailedTemplate;
        const wchar_t* recoveryVerify;
    };

    const wchar_t* GetField(const LocalizedStrings& s, StringId id)
    {
        switch (id)
        {
        case StringId::ExtractDialog:               return s.extractDialog;
        case StringId::ExtractHereFlat:              return s.extractHereFlat;
        case StringId::ExtractHereIntelligent:       return s.extractHereIntelligent;
        case StringId::ExtractFolderFallback:        return s.extractFolderFallback;
        case StringId::ExtractFolderMultiFallback:   return s.extractFolderMultiFallback;
        case StringId::ExtractFolderNamedTemplate:   return s.extractFolderNamedTemplate;
        case StringId::CompressDialog:               return s.compressDialog;
        case StringId::ArchiveFallback:              return s.archiveFallback;
        case StringId::ArchiveNamedTemplate:          return s.archiveNamedTemplate;
        case StringId::TestArchive:                   return s.testArchive;
        case StringId::ScanArchive:                   return s.scanArchive;
        case StringId::BrowseArchive:                 return s.browseArchive;
        case StringId::HashSubmenu:                   return s.hashSubmenu;
        case StringId::SelectionNotOnDisk:            return s.selectionNotOnDisk;
        case StringId::LaunchFailedTemplate:          return s.launchFailedTemplate;
        case StringId::RecoveryVerify:                return s.recoveryVerify;
        }
        return L"";
    }

    // Every locale below supplies all 16 fields (no per-field fallback needed within a known
    // locale) - only a wholly unrecognized locale tag falls back to the en-US row, in
    // GetLocalizedString(id, localeTag) below. Keyed by the same BCP-47 tags as
    // Archiver.App/Strings/<locale>/ (T-F91).
    const std::unordered_map<std::wstring, LocalizedStrings>& GetTable()
    {
        static const std::unordered_map<std::wstring, LocalizedStrings> table = {
            { L"en-US", { L"Extract…", L"Extract here", L"Extract to current folder (Intelligently)", L"Extract to folder", L"Extract each to its own folder", L"Extract to \"{0}\"", L"Compress…", L"Add to archive…", L"Add to \"{0}\"", L"Test archive", L"Scan for threats", L"Open", L"Hash", L"Some of the selected items are not files or folders on a disk (for example, items on a phone or inside a compressed folder), so Pakko did not run this command.", L"Pakko could not start this command (error {0}).", L"Verify with PAR2" } },
            { L"ar-SA", { L"استخراج…", L"استخراج هنا", L"استخراج إلى المجلد الحالي (بذكاء)", L"استخراج إلى مجلد", L"استخراج كل أرشيف إلى مجلده الخاص", L"استخراج إلى \"{0}\"", L"ضغط…", L"إضافة إلى الأرشيف…", L"إضافة إلى \"{0}\"", L"اختبار الأرشيف", L"فحص التهديدات", L"فتح", L"تجزئة", L"\u0628\u0639\u0636 \u0627\u0644\u0639\u0646\u0627\u0635\u0631 \u0627\u0644\u0645\u062D\u062F\u062F\u0629 \u0644\u064A\u0633\u062A \u0645\u0644\u0641\u0627\u062A \u0623\u0648 \u0645\u062C\u0644\u062F\u0627\u062A \u0639\u0644\u0649 \u0642\u0631\u0635 (\u0645\u062B\u0644 \u0627\u0644\u0639\u0646\u0627\u0635\u0631 \u0627\u0644\u0645\u0648\u062C\u0648\u062F\u0629 \u0639\u0644\u0649 \u0647\u0627\u062A\u0641 \u0623\u0648 \u062F\u0627\u062E\u0644 \u0645\u062C\u0644\u062F \u0645\u0636\u063A\u0648\u0637)\u060C \u0644\u0630\u0644\u0643 \u0644\u0645 \u064A\u0634\u063A\u0651\u0644 Pakko \u0647\u0630\u0627 \u0627\u0644\u0623\u0645\u0631.", L"\u062A\u0639\u0630\u0651\u0631 \u0639\u0644\u0649 Pakko \u0628\u062F\u0621 \u0647\u0630\u0627 \u0627\u0644\u0623\u0645\u0631 (\u0627\u0644\u062E\u0637\u0623 {0}).", L"التحقق باستخدام PAR2" } },
            { L"bg-BG", { L"Извличане…", L"Извличане тук", L"Извличане в текущата папка (Интелигентно)", L"Извличане в папка", L"Извличане на всеки архив в собствена папка", L"Извличане в \"{0}\"", L"Компресиране…", L"Добавяне към архив…", L"Добавяне към \"{0}\"", L"Тестване на архива", L"Сканиране за заплахи", L"Отваряне", L"Хеш", L"\u041D\u044F\u043A\u043E\u0438 \u043E\u0442 \u0438\u0437\u0431\u0440\u0430\u043D\u0438\u0442\u0435 \u0435\u043B\u0435\u043C\u0435\u043D\u0442\u0438 \u043D\u0435 \u0441\u0430 \u0444\u0430\u0439\u043B\u043E\u0432\u0435 \u0438\u043B\u0438 \u043F\u0430\u043F\u043A\u0438 \u043D\u0430 \u0434\u0438\u0441\u043A (\u043D\u0430\u043F\u0440\u0438\u043C\u0435\u0440 \u0435\u043B\u0435\u043C\u0435\u043D\u0442\u0438 \u0432 \u0442\u0435\u043B\u0435\u0444\u043E\u043D \u0438\u043B\u0438 \u0432 \u043A\u043E\u043C\u043F\u0440\u0435\u0441\u0438\u0440\u0430\u043D\u0430 \u043F\u0430\u043F\u043A\u0430), \u0437\u0430\u0442\u043E\u0432\u0430 Pakko \u043D\u0435 \u0438\u0437\u043F\u044A\u043B\u043D\u0438 \u0442\u0430\u0437\u0438 \u043A\u043E\u043C\u0430\u043D\u0434\u0430.", L"Pakko \u043D\u0435 \u043C\u043E\u0436\u0430 \u0434\u0430 \u0441\u0442\u0430\u0440\u0442\u0438\u0440\u0430 \u0442\u0430\u0437\u0438 \u043A\u043E\u043C\u0430\u043D\u0434\u0430 (\u0433\u0440\u0435\u0448\u043A\u0430 {0}).", L"Проверка с PAR2" } },
            { L"cs-CZ", { L"Extrahovat…", L"Extrahovat sem", L"Extrahovat do aktuální složky (Inteligentně)", L"Extrahovat do složky", L"Extrahovat každý do vlastní složky", L"Extrahovat do \"{0}\"", L"Komprimovat…", L"Přidat do archivu…", L"Přidat do \"{0}\"", L"Otestovat archiv", L"Zkontrolovat hrozby", L"Otevřít", L"Hash", L"N\u011Bkter\u00E9 vybran\u00E9 polo\u017Eky nejsou soubory ani slo\u017Eky na disku (nap\u0159\u00EDklad polo\u017Eky v telefonu nebo uvnit\u0159 komprimovan\u00E9 slo\u017Eky), proto Pakko tento p\u0159\u00EDkaz nespustil.", L"Pakko nemohl spustit tento p\u0159\u00EDkaz (chyba {0}).", L"Ověřit pomocí PAR2" } },
            { L"da-DK", { L"Udpak…", L"Udpak her", L"Udpak til den aktuelle mappe (Intelligent)", L"Udpak til mappe", L"Udpak hvert arkiv til sin egen mappe", L"Udpak til \"{0}\"", L"Komprimer…", L"Føj til arkiv…", L"Føj til \"{0}\"", L"Test arkiv", L"Scan for trusler", L"Åbn", L"Hash", L"Nogle af de valgte elementer er ikke filer eller mapper p\u00E5 et drev (f.eks. elementer p\u00E5 en telefon eller i en komprimeret mappe), s\u00E5 Pakko k\u00F8rte ikke denne kommando.", L"Pakko kunne ikke starte denne kommando (fejl {0}).", L"Kontrollér med PAR2" } },
            { L"de-DE", { L"Extrahieren…", L"Hier extrahieren", L"In aktuellen Ordner extrahieren (Intelligent)", L"In Ordner extrahieren", L"Jedes Archiv in einen eigenen Ordner extrahieren", L"Extrahieren nach \"{0}\"", L"Komprimieren…", L"Zu Archiv hinzufügen…", L"Zu \"{0}\" hinzufügen", L"Archiv testen", L"Auf Bedrohungen prüfen", L"Öffnen", L"Hash", L"Einige der ausgew\u00E4hlten Elemente sind keine Dateien oder Ordner auf einem Laufwerk (zum Beispiel Elemente auf einem Telefon oder in einem komprimierten Ordner). Pakko hat diesen Befehl daher nicht ausgef\u00FChrt.", L"Pakko konnte diesen Befehl nicht starten (Fehler {0}).", L"Mit PAR2 prüfen" } },
            { L"el-GR", { L"Εξαγωγή…", L"Εξαγωγή εδώ", L"Εξαγωγή στον τρέχοντα φάκελο (Έξυπνα)", L"Εξαγωγή σε φάκελο", L"Εξαγωγή κάθε αρχείου συμπίεσης στον δικό του φάκελο", L"Εξαγωγή στο \"{0}\"", L"Συμπίεση…", L"Προσθήκη στο αρχείο συμπίεσης…", L"Προσθήκη στο \"{0}\"", L"Δοκιμή αρχείου συμπίεσης", L"Έλεγχος για απειλές", L"Άνοιγμα", L"Κατακερματισμός", L"\u039F\u03C1\u03B9\u03C3\u03BC\u03AD\u03BD\u03B1 \u03B1\u03C0\u03CC \u03C4\u03B1 \u03B5\u03C0\u03B9\u03BB\u03B5\u03B3\u03BC\u03AD\u03BD\u03B1 \u03C3\u03C4\u03BF\u03B9\u03C7\u03B5\u03AF\u03B1 \u03B4\u03B5\u03BD \u03B5\u03AF\u03BD\u03B1\u03B9 \u03B1\u03C1\u03C7\u03B5\u03AF\u03B1 \u03AE \u03C6\u03AC\u03BA\u03B5\u03BB\u03BF\u03B9 \u03C3\u03B5 \u03B4\u03AF\u03C3\u03BA\u03BF (\u03B3\u03B9\u03B1 \u03C0\u03B1\u03C1\u03AC\u03B4\u03B5\u03B9\u03B3\u03BC\u03B1, \u03C3\u03C4\u03BF\u03B9\u03C7\u03B5\u03AF\u03B1 \u03C3\u03B5 \u03C4\u03B7\u03BB\u03AD\u03C6\u03C9\u03BD\u03BF \u03AE \u03BC\u03AD\u03C3\u03B1 \u03C3\u03B5 \u03C3\u03C5\u03BC\u03C0\u03B9\u03B5\u03C3\u03BC\u03AD\u03BD\u03BF \u03C6\u03AC\u03BA\u03B5\u03BB\u03BF), \u03BF\u03C0\u03CC\u03C4\u03B5 \u03C4\u03BF Pakko \u03B4\u03B5\u03BD \u03B5\u03BA\u03C4\u03AD\u03BB\u03B5\u03C3\u03B5 \u03B1\u03C5\u03C4\u03AE \u03C4\u03B7\u03BD \u03B5\u03BD\u03C4\u03BF\u03BB\u03AE.", L"\u03A4\u03BF Pakko \u03B4\u03B5\u03BD \u03BC\u03C0\u03CC\u03C1\u03B5\u03C3\u03B5 \u03BD\u03B1 \u03BE\u03B5\u03BA\u03B9\u03BD\u03AE\u03C3\u03B5\u03B9 \u03B1\u03C5\u03C4\u03AE \u03C4\u03B7\u03BD \u03B5\u03BD\u03C4\u03BF\u03BB\u03AE (\u03C3\u03C6\u03AC\u03BB\u03BC\u03B1 {0}).", L"Επαλήθευση με PAR2" } },
            { L"es-ES", { L"Extraer…", L"Extraer aquí", L"Extraer a la carpeta actual (Inteligente)", L"Extraer a una carpeta", L"Extraer cada archivo comprimido a su propia carpeta", L"Extraer a \"{0}\"", L"Comprimir…", L"Añadir al archivo comprimido…", L"Añadir a \"{0}\"", L"Probar archivo comprimido", L"Buscar amenazas", L"Abrir", L"Hash", L"Algunos de los elementos seleccionados no son archivos ni carpetas de un disco (por ejemplo, elementos de un tel\u00E9fono o de una carpeta comprimida), por lo que Pakko no ha ejecutado este comando.", L"Pakko no ha podido iniciar este comando (error {0}).", L"Verificar con PAR2" } },
            { L"et-EE", { L"Paki lahti…", L"Paki lahti siia", L"Paki lahti praegusesse kausta (Intelligentselt)", L"Paki lahti kausta", L"Paki iga arhiiv lahti oma kausta", L"Paki lahti kausta \"{0}\"", L"Tihenda…", L"Lisa arhiivi…", L"Lisa arhiivi \"{0}\"", L"Testi arhiivi", L"Kontrolli ohte", L"Ava", L"Räsi", L"M\u00F5ned valitud \u00FCksused ei ole kettal olevad failid ega kaustad (n\u00E4iteks telefonis v\u00F5i tihendatud kaustas olevad \u00FCksused), seega Pakko seda k\u00E4sku ei k\u00E4ivitanud.", L"Pakko ei saanud seda k\u00E4sku k\u00E4ivitada (t\u00F5rge {0}).", L"Kontrolli PAR2 abil" } },
            { L"fi-FI", { L"Pura…", L"Pura tähän", L"Pura nykyiseen kansioon (Älykkäästi)", L"Pura kansioon", L"Pura jokainen omaan kansioonsa", L"Pura kansioon \"{0}\"", L"Pakkaa…", L"Lisää arkistoon…", L"Lisää arkistoon \"{0}\"", L"Testaa arkisto", L"Tarkista uhat", L"Avaa", L"Tiiviste", L"Osa valituista kohteista ei ole levyll\u00E4 olevia tiedostoja tai kansioita (esimerkiksi puhelimessa tai pakatun kansion sis\u00E4ll\u00E4 olevia kohteita), joten Pakko ei suorittanut t\u00E4t\u00E4 komentoa.", L"Pakko ei voinut k\u00E4ynnist\u00E4\u00E4 t\u00E4t\u00E4 komentoa (virhe {0}).", L"Tarkista PAR2:lla" } },
            { L"fr-FR", { L"Extraire…", L"Extraire ici", L"Extraire vers le dossier actuel (Intelligemment)", L"Extraire vers un dossier", L"Extraire chaque archive vers son propre dossier", L"Extraire vers \"{0}\"", L"Compresser…", L"Ajouter à l'archive…", L"Ajouter à \"{0}\"", L"Tester l'archive", L"Rechercher des menaces", L"Ouvrir", L"Hachage", L"Certains des \u00E9l\u00E9ments s\u00E9lectionn\u00E9s ne sont pas des fichiers ou des dossiers sur un disque (par exemple, des \u00E9l\u00E9ments d'un t\u00E9l\u00E9phone ou d'un dossier compress\u00E9) ; Pakko n'a donc pas ex\u00E9cut\u00E9 cette commande.", L"Pakko n'a pas pu lancer cette commande (erreur {0}).", L"Vérifier avec PAR2" } },
            { L"he-IL", { L"חילוץ…", L"חילוץ לכאן", L"חילוץ לתיקייה הנוכחית (חכם)", L"חילוץ לתיקייה", L"חילוץ כל ארכיון לתיקייה משלו", L"חילוץ אל \"{0}\"", L"דחיסה…", L"הוספה לארכיון…", L"הוספה אל \"{0}\"", L"בדיקת ארכיון", L"סריקה לאיתור איומים", L"פתיחה", L"גיבוב", L"\u05D7\u05DC\u05E7 \u05DE\u05D4\u05E4\u05E8\u05D9\u05D8\u05D9\u05DD \u05E9\u05E0\u05D1\u05D7\u05E8\u05D5 \u05D0\u05D9\u05E0\u05DD \u05E7\u05D1\u05E6\u05D9\u05DD \u05D0\u05D5 \u05EA\u05D9\u05E7\u05D9\u05D5\u05EA \u05D1\u05DB\u05D5\u05E0\u05DF (\u05DC\u05DE\u05E9\u05DC \u05E4\u05E8\u05D9\u05D8\u05D9\u05DD \u05D1\u05D8\u05DC\u05E4\u05D5\u05DF \u05D0\u05D5 \u05D1\u05EA\u05D5\u05DA \u05EA\u05D9\u05E7\u05D9\u05D9\u05D4 \u05D3\u05D7\u05D5\u05E1\u05D4), \u05D5\u05DC\u05DB\u05DF Pakko \u05DC\u05D0 \u05D4\u05E4\u05E2\u05D9\u05DC \u05D0\u05EA \u05D4\u05E4\u05E7\u05D5\u05D3\u05D4.", L"Pakko \u05DC\u05D0 \u05D4\u05E6\u05DC\u05D9\u05D7 \u05DC\u05D4\u05E4\u05E2\u05D9\u05DC \u05D0\u05EA \u05D4\u05E4\u05E7\u05D5\u05D3\u05D4 (\u05E9\u05D2\u05D9\u05D0\u05D4 {0}).", L"אימות באמצעות PAR2" } },
            { L"hi-IN", { L"निकालें…", L"यहां निकालें", L"वर्तमान फ़ोल्डर में निकालें (बुद्धिमानी से)", L"फ़ोल्डर में निकालें", L"प्रत्येक संग्रह को अपने फ़ोल्डर में निकालें", L"\"{0}\" में निकालें", L"संपीड़ित करें…", L"संग्रह में जोड़ें…", L"\"{0}\" में जोड़ें", L"संग्रह का परीक्षण करें", L"खतरों के लिए स्कैन करें", L"खोलें", L"हैश", L"\u091A\u092F\u0928\u093F\u0924 \u0906\u0907\u091F\u092E \u092E\u0947\u0902 \u0938\u0947 \u0915\u0941\u091B \u0921\u093F\u0938\u094D\u0915 \u092A\u0930 \u092E\u094C\u091C\u0942\u0926 \u092B\u093C\u093E\u0907\u0932\u0947\u0902 \u092F\u093E \u092B\u093C\u094B\u0932\u094D\u0921\u0930 \u0928\u0939\u0940\u0902 \u0939\u0948\u0902 (\u0909\u0926\u093E\u0939\u0930\u0923 \u0915\u0947 \u0932\u093F\u090F, \u092B\u093C\u094B\u0928 \u092A\u0930 \u092F\u093E \u0938\u0902\u092A\u0940\u0921\u093C\u093F\u0924 \u092B\u093C\u094B\u0932\u094D\u0921\u0930 \u0915\u0947 \u0905\u0902\u0926\u0930 \u0915\u0947 \u0906\u0907\u091F\u092E), \u0907\u0938\u0932\u093F\u090F Pakko \u0928\u0947 \u092F\u0939 \u0915\u092E\u093E\u0902\u0921 \u0928\u0939\u0940\u0902 \u091A\u0932\u093E\u092F\u093E\u0964", L"Pakko \u092F\u0939 \u0915\u092E\u093E\u0902\u0921 \u092A\u094D\u0930\u093E\u0930\u0902\u092D \u0928\u0939\u0940\u0902 \u0915\u0930 \u0938\u0915\u093E (\u0924\u094D\u0930\u0941\u091F\u093F {0})\u0964", L"PAR2 से सत्यापित करें" } },
            { L"hr-HR", { L"Raspakiraj…", L"Raspakiraj ovdje", L"Raspakiraj u trenutnu mapu (Inteligentno)", L"Raspakiraj u mapu", L"Raspakiraj svaku arhivu u vlastitu mapu", L"Raspakiraj u \"{0}\"", L"Komprimiraj…", L"Dodaj u arhivu…", L"Dodaj u \"{0}\"", L"Testiraj arhivu", L"Provjeri prijetnje", L"Otvori", L"Sažetak", L"Neke od odabranih stavki nisu datoteke ili mape na disku (na primjer, stavke na telefonu ili unutar komprimirane mape), pa Pakko nije pokrenuo ovu naredbu.", L"Pakko nije mogao pokrenuti ovu naredbu (pogre\u0161ka {0}).", L"Provjeri pomoću PAR2" } },
            { L"hu-HU", { L"Kibontás…", L"Kibontás ide", L"Kibontás az aktuális mappába (Intelligensen)", L"Kibontás mappába", L"Minden archívum kibontása saját mappájába", L"Kibontás ide: \"{0}\"", L"Tömörítés…", L"Hozzáadás az archívumhoz…", L"Hozzáadás ehhez: \"{0}\"", L"Archívum tesztelése", L"Fenyegetések keresése", L"Megnyitás", L"Hash", L"A kijel\u00F6lt elemek n\u00E9melyike nem lemezen l\u00E9v\u0151 f\u00E1jl vagy mappa (p\u00E9ld\u00E1ul egy telefonon vagy t\u00F6m\u00F6r\u00EDtett mapp\u00E1ban l\u00E9v\u0151 elem), ez\u00E9rt a Pakko nem futtatta ezt a parancsot.", L"A Pakko nem tudta elind\u00EDtani ezt a parancsot (hiba: {0}).", L"Ellenőrzés PAR2-vel" } },
            { L"id-ID", { L"Ekstrak…", L"Ekstrak di sini", L"Ekstrak ke folder saat ini (Cerdas)", L"Ekstrak ke folder", L"Ekstrak setiap arsip ke foldernya sendiri", L"Ekstrak ke \"{0}\"", L"Kompres…", L"Tambahkan ke arsip…", L"Tambahkan ke \"{0}\"", L"Uji arsip", L"Pindai ancaman", L"Buka", L"Hash", L"Beberapa item yang dipilih bukan file atau folder di disk (misalnya item di ponsel atau di dalam folder terkompresi), sehingga Pakko tidak menjalankan perintah ini.", L"Pakko tidak dapat memulai perintah ini (kesalahan {0}).", L"Verifikasi dengan PAR2" } },
            { L"it-IT", { L"Estrai…", L"Estrai qui", L"Estrai nella cartella corrente (Intelligente)", L"Estrai in una cartella", L"Estrai ogni archivio nella propria cartella", L"Estrai in \"{0}\"", L"Comprimi…", L"Aggiungi all'archivio…", L"Aggiungi a \"{0}\"", L"Testa archivio", L"Verifica minacce", L"Apri", L"Hash", L"Alcuni degli elementi selezionati non sono file o cartelle su un disco (ad esempio elementi di un telefono o di una cartella compressa), quindi Pakko non ha eseguito questo comando.", L"Pakko non \u00E8 riuscito ad avviare questo comando (errore {0}).", L"Verifica con PAR2" } },
            { L"ja-JP", { L"展開…", L"ここに展開", L"現在のフォルダーに展開 (インテリジェント)", L"フォルダーに展開", L"各アーカイブを専用フォルダーに展開", L"\"{0}\" に展開", L"圧縮…", L"アーカイブに追加…", L"\"{0}\" に追加", L"アーカイブをテスト", L"脅威をスキャン", L"開く", L"ハッシュ", L"\u9078\u629E\u3057\u305F\u9805\u76EE\u306E\u4E00\u90E8\u306F\u30C7\u30A3\u30B9\u30AF\u4E0A\u306E\u30D5\u30A1\u30A4\u30EB\u307E\u305F\u306F\u30D5\u30A9\u30EB\u30C0\u30FC\u3067\u306F\u3042\u308A\u307E\u305B\u3093 (\u30B9\u30DE\u30FC\u30C8\u30D5\u30A9\u30F3\u4E0A\u306E\u9805\u76EE\u3084\u5727\u7E2E\u30D5\u30A9\u30EB\u30C0\u30FC\u5185\u306E\u9805\u76EE\u306A\u3069)\u3002\u305D\u306E\u305F\u3081\u3001Pakko \u306F\u3053\u306E\u30B3\u30DE\u30F3\u30C9\u3092\u5B9F\u884C\u3057\u307E\u305B\u3093\u3067\u3057\u305F\u3002", L"Pakko \u306F\u3053\u306E\u30B3\u30DE\u30F3\u30C9\u3092\u958B\u59CB\u3067\u304D\u307E\u305B\u3093\u3067\u3057\u305F (\u30A8\u30E9\u30FC {0})\u3002", L"PAR2 で検証" } },
            { L"ko-KR", { L"압축 풀기…", L"여기에 압축 풀기", L"현재 폴더에 압축 풀기 (지능형)", L"폴더로 압축 풀기", L"각 압축 파일을 고유 폴더로 압축 풀기", L"\"{0}\"(으)로 압축 풀기", L"압축…", L"압축 파일에 추가…", L"\"{0}\"에 추가", L"압축 파일 테스트", L"위협 검사", L"열기", L"해시", L"\uC120\uD0DD\uD55C \uD56D\uBAA9 \uC911 \uC77C\uBD80\uB294 \uB514\uC2A4\uD06C\uC5D0 \uC788\uB294 \uD30C\uC77C\uC774\uB098 \uD3F4\uB354\uAC00 \uC544\uB2D9\uB2C8\uB2E4(\uC608: \uD734\uB300\uD3F0\uC5D0 \uC788\uAC70\uB098 \uC555\uCD95 \uD3F4\uB354 \uC548\uC5D0 \uC788\uB294 \uD56D\uBAA9). \uB530\uB77C\uC11C Pakko\uC5D0\uC11C \uC774 \uBA85\uB839\uC744 \uC2E4\uD589\uD558\uC9C0 \uC54A\uC558\uC2B5\uB2C8\uB2E4.", L"Pakko\uC5D0\uC11C \uC774 \uBA85\uB839\uC744 \uC2DC\uC791\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4(\uC624\uB958 {0}).", L"PAR2로 검증" } },
            { L"lt-LT", { L"Išskleisti…", L"Išskleisti čia", L"Išskleisti į dabartinį aplanką (Išmaniai)", L"Išskleisti į aplanką", L"Išskleisti kiekvieną archyvą į savą aplanką", L"Išskleisti į \"{0}\"", L"Suspausti…", L"Pridėti į archyvą…", L"Pridėti į \"{0}\"", L"Testuoti archyvą", L"Tikrinti grėsmes", L"Atverti", L"Maiša", L"Kai kurie pasirinkti elementai n\u0117ra failai ar aplankai diske (pavyzd\u017Eiui, elementai telefone arba suspaustame aplanke), tod\u0117l Pakko \u0161ios komandos nevykd\u0117.", L"Pakko nepavyko paleisti \u0161ios komandos (klaida {0}).", L"Tikrinti su PAR2" } },
            { L"lv-LV", { L"Izvilkt…", L"Izvilkt šeit", L"Izvilkt uz pašreizējo mapi (Intelektāli)", L"Izvilkt uz mapi", L"Izvilkt katru arhīvu savā mapē", L"Izvilkt uz \"{0}\"", L"Saspiest…", L"Pievienot arhīvam…", L"Pievienot arhīvam \"{0}\"", L"Testēt arhīvu", L"Pārbaudīt draudus", L"Atvērt", L"Kontrolsumma", L"Da\u017Ei atlas\u012Btie vienumi nav faili vai mapes disk\u0101 (piem\u0113ram, vienumi t\u0101lrun\u012B vai saspiest\u0101 map\u0113), t\u0101p\u0113c Pakko \u0161o komandu neizpild\u012Bja.", L"Pakko nevar\u0113ja palaist \u0161o komandu (k\u013C\u016Bda {0}).", L"Pārbaudīt ar PAR2" } },
            { L"nb-NO", { L"Pakk ut…", L"Pakk ut her", L"Pakk ut til gjeldende mappe (Intelligent)", L"Pakk ut til mappe", L"Pakk ut hvert arkiv til sin egen mappe", L"Pakk ut til \"{0}\"", L"Komprimer…", L"Legg til i arkiv…", L"Legg til i \"{0}\"", L"Test arkiv", L"Se etter trusler", L"Åpne", L"Hash", L"Noen av de valgte elementene er ikke filer eller mapper p\u00E5 en disk (for eksempel elementer p\u00E5 en telefon eller i en komprimert mappe), s\u00E5 Pakko kj\u00F8rte ikke denne kommandoen.", L"Pakko kunne ikke starte denne kommandoen (feil {0}).", L"Kontroller med PAR2" } },
            { L"nl-NL", { L"Uitpakken…", L"Hier uitpakken", L"Uitpakken naar huidige map (Intelligent)", L"Uitpakken naar map", L"Elk archief naar eigen map uitpakken", L"Uitpakken naar \"{0}\"", L"Comprimeren…", L"Toevoegen aan archief…", L"Toevoegen aan \"{0}\"", L"Archief testen", L"Scannen op bedreigingen", L"Openen", L"Hash", L"Sommige geselecteerde items zijn geen bestanden of mappen op een schijf (bijvoorbeeld items op een telefoon of in een gecomprimeerde map), dus Pakko heeft deze opdracht niet uitgevoerd.", L"Pakko kon deze opdracht niet starten (fout {0}).", L"Controleren met PAR2" } },
            { L"pl-PL", { L"Wypakuj…", L"Wypakuj tutaj", L"Wypakuj do bieżącego folderu (Inteligentnie)", L"Wypakuj do folderu", L"Wypakuj każde archiwum do własnego folderu", L"Wypakuj do \"{0}\"", L"Kompresuj…", L"Dodaj do archiwum…", L"Dodaj do \"{0}\"", L"Testuj archiwum", L"Skanuj w poszukiwaniu zagrożeń", L"Otwórz", L"Suma kontrolna", L"Niekt\u00F3re z zaznaczonych element\u00F3w nie s\u0105 plikami ani folderami na dysku (na przyk\u0142ad elementy w telefonie lub w folderze skompresowanym), dlatego Pakko nie wykona\u0142 tego polecenia.", L"Pakko nie m\u00F3g\u0142 uruchomi\u0107 tego polecenia (b\u0142\u0105d {0}).", L"Zweryfikuj za pomocą PAR2" } },
            { L"pt-PT", { L"Extrair…", L"Extrair aqui", L"Extrair para a pasta atual (Inteligentemente)", L"Extrair para uma pasta", L"Extrair cada arquivo para a sua própria pasta", L"Extrair para \"{0}\"", L"Comprimir…", L"Adicionar ao arquivo…", L"Adicionar a \"{0}\"", L"Testar arquivo", L"Procurar ameaças", L"Abrir", L"Hash", L"Alguns dos itens selecionados n\u00E3o s\u00E3o ficheiros nem pastas num disco (por exemplo, itens num telem\u00F3vel ou dentro de uma pasta comprimida), pelo que o Pakko n\u00E3o executou este comando.", L"O Pakko n\u00E3o conseguiu iniciar este comando (erro {0}).", L"Verificar com PAR2" } },
            { L"ro-RO", { L"Extrage…", L"Extrage aici", L"Extrage în folderul curent (Inteligent)", L"Extrage într-un folder", L"Extrage fiecare arhivă în propriul folder", L"Extrage în \"{0}\"", L"Comprimă…", L"Adaugă la arhivă…", L"Adaugă la \"{0}\"", L"Testează arhiva", L"Verifică amenințările", L"Deschide", L"Sumă de control", L"Unele dintre elementele selectate nu sunt fi\u0219iere sau foldere de pe un disc (de exemplu, elemente de pe un telefon sau dintr-un folder comprimat), a\u0219a c\u0103 Pakko nu a executat aceast\u0103 comand\u0103.", L"Pakko nu a putut porni aceast\u0103 comand\u0103 (eroare {0}).", L"Verifică cu PAR2" } },
            { L"sk-SK", { L"Extrahovať…", L"Extrahovať sem", L"Extrahovať do aktuálneho priečinka (Inteligentne)", L"Extrahovať do priečinka", L"Extrahovať každý archív do vlastného priečinka", L"Extrahovať do \"{0}\"", L"Komprimovať…", L"Pridať do archívu…", L"Pridať do \"{0}\"", L"Otestovať archív", L"Skontrolovať hrozby", L"Otvoriť", L"Hash", L"Niektor\u00E9 z vybrat\u00FDch polo\u017Eiek nie s\u00FA s\u00FAbory ani prie\u010Dinky na disku (napr\u00EDklad polo\u017Eky v telef\u00F3ne alebo v komprimovanom prie\u010Dinku), preto Pakko tento pr\u00EDkaz nespustil.", L"Pakko nemohol spusti\u0165 tento pr\u00EDkaz (chyba {0}).", L"Overiť pomocou PAR2" } },
            { L"sl-SI", { L"Razširi…", L"Razširi tukaj", L"Razširi v trenutno mapo (Inteligentno)", L"Razširi v mapo", L"Razširi vsak arhiv v svojo mapo", L"Razširi v \"{0}\"", L"Stisni…", L"Dodaj v arhiv…", L"Dodaj v \"{0}\"", L"Preizkusi arhiv", L"Preveri grožnje", L"Odpri", L"Zgoščena vrednost", L"Nekateri izbrani elementi niso datoteke ali mape na disku (na primer elementi v telefonu ali v stisnjeni mapi), zato Pakko tega ukaza ni zagnal.", L"Pakko ni mogel zagnati tega ukaza (napaka {0}).", L"Preveri s PAR2" } },
            { L"sr-Latn-RS", { L"Raspakuj…", L"Raspakuj ovde", L"Raspakuj u trenutnu fasciklu (Inteligentno)", L"Raspakuj u fasciklu", L"Raspakuj svaku arhivu u sopstvenu fasciklu", L"Raspakuj u \"{0}\"", L"Kompresuj…", L"Dodaj u arhivu…", L"Dodaj u \"{0}\"", L"Testiraj arhivu", L"Proveri pretnje", L"Otvori", L"Heš", L"Neke od izabranih stavki nisu datoteke ili fascikle na disku (na primer, stavke na telefonu ili unutar kompresovane fascikle), pa Pakko nije pokrenuo ovu komandu.", L"Pakko nije mogao da pokrene ovu komandu (gre\u0161ka {0}).", L"Proveri pomoću PAR2" } },
            { L"sv-SE", { L"Extrahera…", L"Extrahera hit", L"Extrahera till aktuell mapp (Smart)", L"Extrahera till mapp", L"Extrahera varje arkiv till sin egen mapp", L"Extrahera till \"{0}\"", L"Komprimera…", L"Lägg till i arkiv…", L"Lägg till i \"{0}\"", L"Testa arkiv", L"Sök efter hot", L"Öppna", L"Hash", L"N\u00E5gra av de markerade objekten \u00E4r inte filer eller mappar p\u00E5 en disk (till exempel objekt i en telefon eller i en komprimerad mapp), s\u00E5 Pakko k\u00F6rde inte det h\u00E4r kommandot.", L"Pakko kunde inte starta det h\u00E4r kommandot (fel {0}).", L"Verifiera med PAR2" } },
            { L"sw-KE", { L"Toa…", L"Toa hapa", L"Toa kwenye folda ya sasa (Kwa Akili)", L"Toa kwenye folda", L"Toa kila kumbukumbu kwenye folda yake", L"Toa kwenye \"{0}\"", L"Bana…", L"Ongeza kwenye kumbukumbu…", L"Ongeza kwenye \"{0}\"", L"Jaribu kumbukumbu", L"Chunguza vitisho", L"Fungua", L"Heshi", L"Baadhi ya vipengee ulivyochagua si faili au folda zilizo kwenye diski (kwa mfano, vipengee vilivyo kwenye simu au ndani ya folda iliyobanwa), kwa hivyo Pakko haikuendesha amri hii.", L"Pakko haikuweza kuanzisha amri hii (hitilafu {0}).", L"Thibitisha kwa PAR2" } },
            { L"th-TH", { L"แตกไฟล์…", L"แตกไฟล์ที่นี่", L"แตกไฟล์ไปยังโฟลเดอร์ปัจจุบัน (อัจฉริยะ)", L"แตกไฟล์ไปยังโฟลเดอร์", L"แตกไฟล์บีบอัดแต่ละไฟล์ไปยังโฟลเดอร์ของตัวเอง", L"แตกไฟล์ไปยัง \"{0}\"", L"บีบอัด…", L"เพิ่มลงในไฟล์บีบอัด…", L"เพิ่มลงใน \"{0}\"", L"ทดสอบไฟล์บีบอัด", L"สแกนหาภัยคุกคาม", L"เปิด", L"แฮช", L"\u0E23\u0E32\u0E22\u0E01\u0E32\u0E23\u0E17\u0E35\u0E48\u0E40\u0E25\u0E37\u0E2D\u0E01\u0E1A\u0E32\u0E07\u0E23\u0E32\u0E22\u0E01\u0E32\u0E23\u0E44\u0E21\u0E48\u0E43\u0E0A\u0E48\u0E44\u0E1F\u0E25\u0E4C\u0E2B\u0E23\u0E37\u0E2D\u0E42\u0E1F\u0E25\u0E40\u0E14\u0E2D\u0E23\u0E4C\u0E1A\u0E19\u0E14\u0E34\u0E2A\u0E01\u0E4C (\u0E40\u0E0A\u0E48\u0E19 \u0E23\u0E32\u0E22\u0E01\u0E32\u0E23\u0E43\u0E19\u0E42\u0E17\u0E23\u0E28\u0E31\u0E1E\u0E17\u0E4C\u0E2B\u0E23\u0E37\u0E2D\u0E20\u0E32\u0E22\u0E43\u0E19\u0E42\u0E1F\u0E25\u0E40\u0E14\u0E2D\u0E23\u0E4C\u0E17\u0E35\u0E48\u0E1A\u0E35\u0E1A\u0E2D\u0E31\u0E14) Pakko \u0E08\u0E36\u0E07\u0E44\u0E21\u0E48\u0E44\u0E14\u0E49\u0E40\u0E23\u0E35\u0E22\u0E01\u0E43\u0E0A\u0E49\u0E04\u0E33\u0E2A\u0E31\u0E48\u0E07\u0E19\u0E35\u0E49", L"Pakko \u0E44\u0E21\u0E48\u0E2A\u0E32\u0E21\u0E32\u0E23\u0E16\u0E40\u0E23\u0E34\u0E48\u0E21\u0E04\u0E33\u0E2A\u0E31\u0E48\u0E07\u0E19\u0E35\u0E49\u0E44\u0E14\u0E49 (\u0E02\u0E49\u0E2D\u0E1C\u0E34\u0E14\u0E1E\u0E25\u0E32\u0E14 {0})", L"ตรวจสอบด้วย PAR2" } },
            { L"tr-TR", { L"Ayıkla…", L"Buraya ayıkla", L"Geçerli klasöre ayıkla (Akıllıca)", L"Klasöre ayıkla", L"Her arşivi kendi klasörüne ayıkla", L"\"{0}\" konumuna ayıkla", L"Sıkıştır…", L"Arşive ekle…", L"\"{0}\" arşivine ekle", L"Arşivi test et", L"Tehditleri tara", L"Aç", L"Karma", L"Se\u00E7ilen \u00F6\u011Felerin baz\u0131lar\u0131 diskteki dosya veya klas\u00F6rler de\u011Fil (\u00F6rne\u011Fin bir telefondaki ya da s\u0131k\u0131\u015Ft\u0131r\u0131lm\u0131\u015F bir klas\u00F6r\u00FCn i\u00E7indeki \u00F6\u011Feler), bu nedenle Pakko bu komutu \u00E7al\u0131\u015Ft\u0131rmad\u0131.", L"Pakko bu komutu ba\u015Flatamad\u0131 (hata {0}).", L"PAR2 ile doğrula" } },
            { L"uk-UA", { L"Видобути файли…", L"Видобути до поточної папки", L"Видобути до поточної папки (Інтелектуально)", L"Видобути до папки", L"Видобути кожен у свою папку", L"Видобути до \"{0}\"", L"Стиснути…", L"Додати до архіву…", L"Додати до \"{0}\"", L"Тестувати архів", L"Перевірити на загрози", L"Відкрити", L"Хеш-суми", L"\u0414\u0435\u044F\u043A\u0456 \u0437 \u0432\u0438\u0431\u0440\u0430\u043D\u0438\u0445 \u0435\u043B\u0435\u043C\u0435\u043D\u0442\u0456\u0432 \u043D\u0435 \u0454 \u0444\u0430\u0439\u043B\u0430\u043C\u0438 \u0447\u0438 \u043F\u0430\u043F\u043A\u0430\u043C\u0438 \u043D\u0430 \u0434\u0438\u0441\u043A\u0443 (\u043D\u0430\u043F\u0440\u0438\u043A\u043B\u0430\u0434, \u0435\u043B\u0435\u043C\u0435\u043D\u0442\u0438 \u043D\u0430 \u0442\u0435\u043B\u0435\u0444\u043E\u043D\u0456 \u0430\u0431\u043E \u0432\u0441\u0435\u0440\u0435\u0434\u0438\u043D\u0456 \u0441\u0442\u0438\u0441\u043D\u0443\u0442\u043E\u0457 \u043F\u0430\u043F\u043A\u0438), \u0442\u043E\u043C\u0443 Pakko \u043D\u0435 \u0432\u0438\u043A\u043E\u043D\u0430\u0432 \u0446\u044E \u043A\u043E\u043C\u0430\u043D\u0434\u0443.", L"Pakko \u043D\u0435 \u0432\u0434\u0430\u043B\u043E\u0441\u044F \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0438 \u0446\u044E \u043A\u043E\u043C\u0430\u043D\u0434\u0443 (\u043F\u043E\u043C\u0438\u043B\u043A\u0430 {0}).", L"Перевірити за PAR2" } },
            { L"ur-PK", { L"نکالیں…", L"یہاں نکالیں", L"موجودہ فولڈر میں نکالیں (ذہانت سے)", L"فولڈر میں نکالیں", L"ہر آرکائیو کو اپنے فولڈر میں نکالیں", L"\"{0}\" میں نکالیں", L"کمپریس کریں…", L"آرکائیو میں شامل کریں…", L"\"{0}\" میں شامل کریں", L"آرکائیو کی جانچ کریں", L"خطرات کے لیے اسکین کریں", L"کھولیں", L"ہیش", L"\u0645\u0646\u062A\u062E\u0628 \u06A9\u0631\u062F\u06C1 \u0622\u0626\u0679\u0645\u0632 \u0645\u06CC\u06BA \u0633\u06D2 \u06A9\u0686\u06BE \u0688\u0633\u06A9 \u067E\u0631 \u0645\u0648\u062C\u0648\u062F \u0641\u0627\u0626\u0644\u06CC\u06BA \u06CC\u0627 \u0641\u0648\u0644\u0688\u0631\u0632 \u0646\u06C1\u06CC\u06BA \u06C1\u06CC\u06BA (\u0645\u062B\u0627\u0644 \u06A9\u06D2 \u0637\u0648\u0631 \u067E\u0631 \u0641\u0648\u0646 \u067E\u0631 \u06CC\u0627 \u06A9\u0645\u067E\u0631\u06CC\u0633\u0688 \u0641\u0648\u0644\u0688\u0631 \u06A9\u06D2 \u0627\u0646\u062F\u0631 \u0645\u0648\u062C\u0648\u062F \u0622\u0626\u0679\u0645\u0632)\u060C \u0627\u0633 \u0644\u06CC\u06D2 Pakko \u0646\u06D2 \u06CC\u06C1 \u06A9\u0645\u0627\u0646\u0688 \u0646\u06C1\u06CC\u06BA \u0686\u0644\u0627\u0626\u06CC\u06D4", L"Pakko \u06CC\u06C1 \u06A9\u0645\u0627\u0646\u0688 \u0634\u0631\u0648\u0639 \u0646\u06C1\u06CC\u06BA \u06A9\u0631 \u0633\u06A9\u0627 (\u062E\u0631\u0627\u0628\u06CC {0})\u06D4", L"PAR2 سے تصدیق کریں" } },
            { L"vi-VN", { L"Giải nén…", L"Giải nén tại đây", L"Giải nén vào thư mục hiện tại (Thông minh)", L"Giải nén vào thư mục", L"Giải nén từng tệp nén vào thư mục riêng", L"Giải nén vào \"{0}\"", L"Nén…", L"Thêm vào tệp nén…", L"Thêm vào \"{0}\"", L"Kiểm tra tệp nén", L"Quét mối đe dọa", L"Mở", L"Mã băm", L"M\u1ED9t s\u1ED1 m\u1EE5c \u0111\u00E3 ch\u1ECDn kh\u00F4ng ph\u1EA3i l\u00E0 t\u1EC7p ho\u1EB7c th\u01B0 m\u1EE5c tr\u00EAn \u1ED5 \u0111\u0129a (v\u00ED d\u1EE5: m\u1EE5c tr\u00EAn \u0111i\u1EC7n tho\u1EA1i ho\u1EB7c b\u00EAn trong th\u01B0 m\u1EE5c n\u00E9n), v\u00EC v\u1EADy Pakko \u0111\u00E3 kh\u00F4ng ch\u1EA1y l\u1EC7nh n\u00E0y.", L"Pakko kh\u00F4ng th\u1EC3 kh\u1EDFi \u0111\u1ED9ng l\u1EC7nh n\u00E0y (l\u1ED7i {0}).", L"Xác minh bằng PAR2" } },
            { L"zh-Hans", { L"解压…", L"解压到当前文件夹", L"智能解压到当前文件夹", L"解压到文件夹", L"将每个压缩包解压到各自的文件夹", L"解压到 \"{0}\"", L"压缩…", L"添加到压缩包…", L"添加到 \"{0}\"", L"测试压缩包", L"扫描威胁", L"打开", L"哈希", L"\u6240\u9009\u9879\u76EE\u4E2D\u6709\u90E8\u5206\u4E0D\u662F\u78C1\u76D8\u4E0A\u7684\u6587\u4EF6\u6216\u6587\u4EF6\u5939\uFF08\u4F8B\u5982\u624B\u673A\u4E2D\u6216\u538B\u7F29\u6587\u4EF6\u5939\u5185\u7684\u9879\u76EE\uFF09\uFF0C\u56E0\u6B64 Pakko \u672A\u8FD0\u884C\u6B64\u547D\u4EE4\u3002", L"Pakko \u65E0\u6CD5\u542F\u52A8\u6B64\u547D\u4EE4\uFF08\u9519\u8BEF {0}\uFF09\u3002", L"使用 PAR2 验证" } },
        };
        return table;
    }
}

namespace
{
    // The user's language list in order of preference; empty when Windows has none recorded.
    std::vector<std::wstring> ReadUserLanguages()
    {
        const wchar_t* const subKey = L"Control Panel\\International\\User Profile";
        DWORD bytes = 0;
        if (RegGetValueW(HKEY_CURRENT_USER, subKey, L"Languages", RRF_RT_REG_MULTI_SZ, nullptr, nullptr, &bytes) != ERROR_SUCCESS
            || bytes < sizeof(wchar_t))
            return {};

        std::vector<wchar_t> buffer(bytes / sizeof(wchar_t) + 2, L'\0');
        if (RegGetValueW(HKEY_CURRENT_USER, subKey, L"Languages", RRF_RT_REG_MULTI_SZ, nullptr, buffer.data(), &bytes) != ERROR_SUCCESS)
            return {};

        std::vector<std::wstring> languages;
        const wchar_t* const end = buffer.data() + buffer.size();
        for (const wchar_t* entry = buffer.data(); entry < end && *entry != L'\0';)
        {
            const size_t length = wcsnlen(entry, static_cast<size_t>(end - entry));
            languages.emplace_back(entry, length);
            entry += length + 1;
        }
        return languages;
    }

    std::wstring GetDisplayLanguageTag();
}

std::wstring GetCurrentUILanguageTag()
{
    return PickLanguageTag(ReadUserLanguages(), GetDisplayLanguageTag());
}

namespace
{
std::wstring GetDisplayLanguageTag()
{
    ULONG numLanguages = 0;
    ULONG bufferSize = 0;
    if (GetThreadPreferredUILanguages(MUI_LANGUAGE_NAME, &numLanguages, nullptr, &bufferSize) && bufferSize > 0)
    {
        std::vector<wchar_t> buffer(bufferSize);
        if (GetThreadPreferredUILanguages(MUI_LANGUAGE_NAME, &numLanguages, buffer.data(), &bufferSize) && numLanguages > 0)
        {
            // MUI_LANGUAGE_NAME yields a MULTI_SZ buffer; the first NUL-terminated entry is the
            // caller's most-preferred UI language, which is all that's needed here (no chained
            // fallback across multiple preferred languages - see Localization.h).
            return std::wstring(buffer.data());
        }
    }
    return L"en-US";
}
}

namespace
{
    bool EqualsIgnoreCase(const std::wstring& a, const std::wstring& b)
    {
        return CompareStringOrdinal(a.c_str(), static_cast<int>(a.size()), b.c_str(), static_cast<int>(b.size()), TRUE) == CSTR_EQUAL;
    }

    // The part of a BCP-47 tag before its first '-', lower-cased ("zh" for "zh-Hant-TW").
    std::wstring LanguageOf(const std::wstring& tag)
    {
        std::wstring language = tag.substr(0, tag.find(L'-'));
        for (wchar_t& c : language)
            c = static_cast<wchar_t>(towlower(c));
        return language;
    }

    // T-F254: same rule as Archiver.Messages' UiCulture — exact tag, then Simplified Chinese for
    // zh-CN/zh-SG/zh-Hans-*, then the table's row for the same language (de-AT -> de-DE), else
    // en-US. Traditional Chinese has no row and must not get the Simplified one.
    // nullptr when the table has no row for the tag's language (it then shows en-US).
    const LocalizedStrings* FindRow(const std::wstring& localeTag)
    {
        const auto& table = GetTable();
        for (const auto& [key, row] : table)
        {
            if (EqualsIgnoreCase(key, localeTag))
                return &row;
        }

        std::wstring language = LanguageOf(localeTag);
        if (language == L"zh")
        {
            const std::wstring rest = localeTag.size() > 3 ? localeTag.substr(3) : std::wstring();
            const std::wstring script = rest.substr(0, rest.find(L'-'));
            const bool simplified = rest.empty() || EqualsIgnoreCase(script, L"Hans")
                || EqualsIgnoreCase(script, L"CN") || EqualsIgnoreCase(script, L"SG");
            return simplified ? &table.at(L"zh-Hans") : nullptr;
        }
        if (language == L"no")
            language = L"nb";

        for (const auto& [key, row] : table)
        {
            if (LanguageOf(key) == language)
                return &row;
        }
        return nullptr;
    }

    const LocalizedStrings& ResolveRow(const std::wstring& localeTag)
    {
        const LocalizedStrings* row = FindRow(localeTag);
        return row != nullptr ? *row : GetTable().at(L"en-US");
    }
}

std::wstring PickLanguageTag(const std::vector<std::wstring>& userLanguages, const std::wstring& displayTag)
{
    if (userLanguages.empty())
        return displayTag;

    for (const std::wstring& tag : userLanguages)
    {
        if (LanguageOf(tag) == L"en")
            return L"en-US";
        if (FindRow(tag) != nullptr)
            return tag;
    }
    return L"en-US";
}

std::wstring GetLocalizedString(StringId id, const std::wstring& localeTag)
{
    return std::wstring(GetField(ResolveRow(localeTag), id));
}

std::wstring GetLocalizedString(StringId id)
{
    return GetLocalizedString(id, GetCurrentUILanguageTag());
}

std::wstring ApplyTemplate(const std::wstring& tmpl, const std::wstring& value)
{
    std::wstring result = tmpl;
    const auto pos = result.find(L"{0}");
    if (pos != std::wstring::npos)
        result.replace(pos, 3, value);
    return result;
}
