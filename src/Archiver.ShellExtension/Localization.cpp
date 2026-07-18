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
        }
        return L"";
    }

    // Every locale below supplies all 10 fields (no per-field fallback needed within a known
    // locale) - only a wholly unrecognized locale tag falls back to the en-US row, in
    // GetLocalizedString(id, localeTag) below. Keyed by the same BCP-47 tags as
    // Archiver.App/Strings/<locale>/ (T-F91).
    const std::unordered_map<std::wstring, LocalizedStrings>& GetTable()
    {
        static const std::unordered_map<std::wstring, LocalizedStrings> table = {
            { L"en-US", { L"Extract…", L"Extract here", L"Extract to current folder (Intelligently)", L"Extract to folder", L"Extract each to its own folder", L"Extract to \"{0}\"", L"Compress…", L"Add to archive…", L"Add to \"{0}\"", L"Test archive" } },
            { L"ar-SA", { L"استخراج…", L"استخراج هنا", L"استخراج إلى المجلد الحالي (بذكاء)", L"استخراج إلى مجلد", L"استخراج كل أرشيف إلى مجلده الخاص", L"استخراج إلى \"{0}\"", L"ضغط…", L"إضافة إلى الأرشيف…", L"إضافة إلى \"{0}\"", L"اختبار الأرشيف" } },
            { L"bg-BG", { L"Извличане…", L"Извличане тук", L"Извличане в текущата папка (Интелигентно)", L"Извличане в папка", L"Извличане на всеки архив в собствена папка", L"Извличане в \"{0}\"", L"Компресиране…", L"Добавяне към архив…", L"Добавяне към \"{0}\"", L"Тестване на архива" } },
            { L"cs-CZ", { L"Extrahovat…", L"Extrahovat sem", L"Extrahovat do aktuální složky (Inteligentně)", L"Extrahovat do složky", L"Extrahovat každý do vlastní složky", L"Extrahovat do \"{0}\"", L"Komprimovat…", L"Přidat do archivu…", L"Přidat do \"{0}\"", L"Otestovat archiv" } },
            { L"da-DK", { L"Udpak…", L"Udpak her", L"Udpak til den aktuelle mappe (Intelligent)", L"Udpak til mappe", L"Udpak hvert arkiv til sin egen mappe", L"Udpak til \"{0}\"", L"Komprimer…", L"Føj til arkiv…", L"Føj til \"{0}\"", L"Test arkiv" } },
            { L"de-DE", { L"Extrahieren…", L"Hier extrahieren", L"In aktuellen Ordner extrahieren (Intelligent)", L"In Ordner extrahieren", L"Jedes Archiv in einen eigenen Ordner extrahieren", L"Extrahieren nach \"{0}\"", L"Komprimieren…", L"Zu Archiv hinzufügen…", L"Zu \"{0}\" hinzufügen", L"Archiv testen" } },
            { L"el-GR", { L"Εξαγωγή…", L"Εξαγωγή εδώ", L"Εξαγωγή στον τρέχοντα φάκελο (Έξυπνα)", L"Εξαγωγή σε φάκελο", L"Εξαγωγή κάθε αρχείου στον δικό του φάκελο", L"Εξαγωγή στο \"{0}\"", L"Συμπίεση…", L"Προσθήκη στο αρχείο…", L"Προσθήκη στο \"{0}\"", L"Δοκιμή αρχείου" } },
            { L"es-ES", { L"Extraer…", L"Extraer aquí", L"Extraer a la carpeta actual (Inteligente)", L"Extraer a una carpeta", L"Extraer cada archivo a su propia carpeta", L"Extraer a \"{0}\"", L"Comprimir…", L"Añadir al archivo…", L"Añadir a \"{0}\"", L"Probar archivo" } },
            { L"et-EE", { L"Paki lahti…", L"Paki lahti siia", L"Paki lahti praegusesse kausta (Intelligentselt)", L"Paki lahti kausta", L"Paki iga arhiiv lahti oma kausta", L"Paki lahti kausta \"{0}\"", L"Tihenda…", L"Lisa arhiivi…", L"Lisa \"{0}\"", L"Testi arhiivi" } },
            { L"fi-FI", { L"Pura…", L"Pura tähän", L"Pura nykyiseen kansioon (Älykkäästi)", L"Pura kansioon", L"Pura jokainen omaan kansioonsa", L"Pura kansioon \"{0}\"", L"Pakkaa…", L"Lisää arkistoon…", L"Lisää arkistoon \"{0}\"", L"Testaa arkisto" } },
            { L"fr-FR", { L"Extraire…", L"Extraire ici", L"Extraire vers le dossier actuel (Intelligemment)", L"Extraire vers un dossier", L"Extraire chaque archive vers son propre dossier", L"Extraire vers \"{0}\"", L"Compresser…", L"Ajouter à l'archive…", L"Ajouter à \"{0}\"", L"Tester l'archive" } },
            { L"he-IL", { L"חילוץ…", L"חילוץ לכאן", L"חילוץ לתיקייה הנוכחית (חכם)", L"חילוץ לתיקייה", L"חילוץ כל ארכיון לתיקייה משלו", L"חילוץ אל \"{0}\"", L"דחיסה…", L"הוספה לארכיון…", L"הוספה אל \"{0}\"", L"בדיקת ארכיון" } },
            { L"hi-IN", { L"निकालें…", L"यहां निकालें", L"वर्तमान फ़ोल्डर में निकालें (बुद्धिमानी से)", L"फ़ोल्डर में निकालें", L"प्रत्येक संग्रह को अपने फ़ोल्डर में निकालें", L"\"{0}\" में निकालें", L"संपीड़ित करें…", L"संग्रह में जोड़ें…", L"\"{0}\" में जोड़ें", L"संग्रह का परीक्षण करें" } },
            { L"hr-HR", { L"Raspakiraj…", L"Raspakiraj ovdje", L"Raspakiraj u trenutnu mapu (Inteligentno)", L"Raspakiraj u mapu", L"Raspakiraj svaku arhivu u vlastitu mapu", L"Raspakiraj u \"{0}\"", L"Komprimiraj…", L"Dodaj u arhivu…", L"Dodaj u \"{0}\"", L"Testiraj arhivu" } },
            { L"hu-HU", { L"Kibontás…", L"Kibontás ide", L"Kibontás az aktuális mappába (Intelligensen)", L"Kibontás mappába", L"Minden archívum kibontása saját mappájába", L"Kibontás ide: \"{0}\"", L"Tömörítés…", L"Hozzáadás az archívumhoz…", L"Hozzáadás ehhez: \"{0}\"", L"Archívum tesztelése" } },
            { L"id-ID", { L"Ekstrak…", L"Ekstrak di sini", L"Ekstrak ke folder saat ini (Cerdas)", L"Ekstrak ke folder", L"Ekstrak setiap arsip ke foldernya sendiri", L"Ekstrak ke \"{0}\"", L"Kompres…", L"Tambahkan ke arsip…", L"Tambahkan ke \"{0}\"", L"Uji arsip" } },
            { L"it-IT", { L"Estrai…", L"Estrai qui", L"Estrai nella cartella corrente (Intelligente)", L"Estrai in una cartella", L"Estrai ogni archivio nella propria cartella", L"Estrai in \"{0}\"", L"Comprimi…", L"Aggiungi all'archivio…", L"Aggiungi a \"{0}\"", L"Verifica archivio" } },
            { L"ja-JP", { L"展開…", L"ここに展開", L"現在のフォルダーに展開 (インテリジェント)", L"フォルダーに展開", L"各書庫を専用フォルダーに展開", L"\"{0}\" に展開", L"圧縮…", L"書庫に追加…", L"\"{0}\" に追加", L"書庫をテスト" } },
            { L"ko-KR", { L"압축 풀기…", L"여기에 압축 풀기", L"현재 폴더에 압축 풀기 (지능형)", L"폴더로 압축 풀기", L"각 압축 파일을 고유 폴더로 압축 풀기", L"\"{0}\"(으)로 압축 풀기", L"압축…", L"압축 파일에 추가…", L"\"{0}\"에 추가", L"압축 파일 테스트" } },
            { L"lt-LT", { L"Išskleisti…", L"Išskleisti čia", L"Išskleisti į dabartinį aplanką (Išmaniai)", L"Išskleisti į aplanką", L"Išskleisti kiekvieną archyvą į savą aplanką", L"Išskleisti į \"{0}\"", L"Suspausti…", L"Pridėti į archyvą…", L"Pridėti į \"{0}\"", L"Tikrinti archyvą" } },
            { L"lv-LV", { L"Izvilkt…", L"Izvilkt šeit", L"Izvilkt uz pašreinējo mapi (Intelektāli)", L"Izvilkt uz mapi", L"Izvilkt katru arhīvu savā mapē", L"Izvilkt uz \"{0}\"", L"Saspiest…", L"Pievienot arhīvam…", L"Pievienot \"{0}\"", L"Testēt arhīvu" } },
            { L"nb-NO", { L"Pakk ut…", L"Pakk ut her", L"Pakk ut til gjeldende mappe (Intelligent)", L"Pakk ut til mappe", L"Pakk ut hvert arkiv til sin egen mappe", L"Pakk ut til \"{0}\"", L"Komprimer…", L"Legg til i arkiv…", L"Legg til i \"{0}\"", L"Test arkiv" } },
            { L"nl-NL", { L"Uitpakken…", L"Hier uitpakken", L"Uitpakken naar huidige map (Intelligent)", L"Uitpakken naar map", L"Elk archief naar eigen map uitpakken", L"Uitpakken naar \"{0}\"", L"Comprimeren…", L"Toevoegen aan archief…", L"Toevoegen aan \"{0}\"", L"Archief testen" } },
            { L"pl-PL", { L"Wypakuj…", L"Wypakuj tutaj", L"Wypakuj do bieżącego folderu (Inteligentnie)", L"Wypakuj do folderu", L"Wypakuj każde archiwum do własnego folderu", L"Wypakuj do \"{0}\"", L"Kompresuj…", L"Dodaj do archiwum…", L"Dodaj do \"{0}\"", L"Testuj archiwum" } },
            { L"pt-PT", { L"Extrair…", L"Extrair aqui", L"Extrair para a pasta atual (Inteligentemente)", L"Extrair para uma pasta", L"Extrair cada arquivo para a sua própria pasta", L"Extrair para \"{0}\"", L"Comprimir…", L"Adicionar ao arquivo…", L"Adicionar a \"{0}\"", L"Testar arquivo" } },
            { L"ro-RO", { L"Extrage…", L"Extrage aici", L"Extrage în folderul curent (Inteligent)", L"Extrage într-un folder", L"Extrage fiecare arhivă în propriul folder", L"Extrage în \"{0}\"", L"Comprimă…", L"Adaugă la arhivă…", L"Adaugă la \"{0}\"", L"Testează arhiva" } },
            { L"sk-SK", { L"Extrahovať…", L"Extrahovať sem", L"Extrahovať do aktuálneho priečinka (Inteligentne)", L"Extrahovať do priečinka", L"Extrahovať každý archív do vlastného priečinka", L"Extrahovať do \"{0}\"", L"Komprimovať…", L"Pridať do archívu…", L"Pridať do \"{0}\"", L"Otestovať archív" } },
            { L"sl-SI", { L"Razširi…", L"Razširi tukaj", L"Razširi v trenutno mapo (Inteligentno)", L"Razširi v mapo", L"Razširi vsak arhiv v svojo mapo", L"Razširi v \"{0}\"", L"Stisni…", L"Dodaj v arhiv…", L"Dodaj v \"{0}\"", L"Preizkusi arhiv" } },
            { L"sr-Latn-RS", { L"Raspakuj…", L"Raspakuj ovde", L"Raspakuj u trenutnu fasciklu (Inteligentno)", L"Raspakuj u fasciklu", L"Raspakuj svaku arhivu u sopstvenu fasciklu", L"Raspakuj u \"{0}\"", L"Kompresuj…", L"Dodaj u arhivu…", L"Dodaj u \"{0}\"", L"Testiraj arhivu" } },
            { L"sv-SE", { L"Extrahera…", L"Extrahera hit", L"Extrahera till aktuell mapp (Smart)", L"Extrahera till mapp", L"Extrahera varje arkiv till sin egen mapp", L"Extrahera till \"{0}\"", L"Komprimera…", L"Lägg till i arkiv…", L"Lägg till i \"{0}\"", L"Testa arkiv" } },
            { L"sw-KE", { L"Toa…", L"Toa hapa", L"Toa kwenye folda ya sasa (Kwa Akili)", L"Toa kwenye folda", L"Toa kila kumbukumbu kwenye folda yake", L"Toa kwenye \"{0}\"", L"Bana…", L"Ongeza kwenye kumbukumbu…", L"Ongeza kwenye \"{0}\"", L"Jaribu kumbukumbu" } },
            { L"th-TH", { L"แตกไฟล์…", L"แตกไฟล์ที่นี่", L"แตกไฟล์ไปยังโฟลเดอร์ปัจจุบัน (อัจฉริยะ)", L"แตกไฟล์ไปยังโฟลเดอร์", L"แตกไฟล์แต่ละไฟล์ไปยังโฟลเดอร์ของตัวเอง", L"แตกไฟล์ไปยัง \"{0}\"", L"บีบอัด…", L"เพิ่มลงในไฟล์บีบอัด…", L"เพิ่มลงใน \"{0}\"", L"ทดสอบไฟล์บีบอัด" } },
            { L"tr-TR", { L"Ayıkla…", L"Buraya ayıkla", L"Geçerli klasöre ayıkla (Akıllıca)", L"Klasöre ayıkla", L"Her arşivi kendi klasörüne ayıkla", L"\"{0}\" konumuna ayıkla", L"Sıkıştır…", L"Arşive ekle…", L"\"{0}\" konumuna ekle", L"Arşivi test et" } },
            { L"uk-UA", { L"Видобути файли…", L"Видобути до поточної папки", L"Видобути до поточної папки (Інтелектуально)", L"Видобути до папки", L"Видобути кожен у свою папку", L"Видобути до \"{0}\"", L"Стиснути…", L"Додати до архіву…", L"Додати до \"{0}\"", L"Тестувати архів" } },
            { L"ur-PK", { L"نکالیں…", L"یہاں نکالیں", L"موجودہ فولڈر میں نکالیں (ذہانت سے)", L"فولڈر میں نکالیں", L"ہر آرکائو کو اپنے فولڈر میں نکالیں", L"\"{0}\" میں نکالیں", L"کمپریس کریں…", L"آرکائو میں شامل کریں…", L"\"{0}\" میں شامل کریں", L"آرکائو کی جانچ کریں" } },
            { L"vi-VN", { L"Giải nén…", L"Giải nén tại đây", L"Giải nén vào thư mục hiện tại (Thông minh)", L"Giải nén vào thư mục", L"Giải nén từng kho lưu trữ vào thư mục riêng", L"Giải nén vào \"{0}\"", L"Nén…", L"Thêm vào kho lưu trữ…", L"Thêm vào \"{0}\"", L"Kiểm tra kho lưu trữ" } },
            { L"zh-Hans", { L"解压…", L"解压到当前文件夹", L"智能解压到当前文件夹", L"解压到文件夹", L"将每个压缩包解压到各自的文件夹", L"解压到 \"{0}\"", L"压缩…", L"添加到压缩包…", L"添加到 \"{0}\"", L"测试压缩包" } },
        };
        return table;
    }
}

std::wstring GetCurrentUILanguageTag()
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

std::wstring GetLocalizedString(StringId id, const std::wstring& localeTag)
{
    const auto& table = GetTable();

    auto it = table.find(localeTag);
    if (it == table.end())
        it = table.find(L"en-US");

    return std::wstring(GetField(it->second, id));
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
