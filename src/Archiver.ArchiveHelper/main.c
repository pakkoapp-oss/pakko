/*
 * T-F470: Pakko's reader over Windows' own libarchive (C:\Windows\System32\archiveint.dll).
 * Runs inside the Pakko.TarSandbox AppContainer with the archive on stdin. Imports only
 * kernel32.dll: a process that loads user32 or ole32 fails to start (0xC0000142) in an
 * AppContainer without a desktop (docs/DECISIONS.md, "T-F468 research done").
 *
 * No name from the archive ever becomes a path here: a selected regular file is written as
 * out\<index>, and Pakko lays the files out itself. Protocol: docs/ARCHITECTURE.md, "Archive helper".
 */
#include <windows.h>

typedef __int64 la_int64_t;
typedef __int64 la_ssize_t;
typedef la_ssize_t(__cdecl *read_cb)(void *, void *, const void **);
typedef la_int64_t(__cdecl *seek_cb)(void *, void *, la_int64_t, int);
typedef int(__cdecl *support_fn)(void *);

#define ARCHIVE_EOF 1
#define ARCHIVE_OK 0
#define ARCHIVE_WARN (-20)
#define AE_IFREG 0100000u

#define EXIT_DONE 0
#define EXIT_ARCHIVE 1
#define EXIT_USAGE 2
#define EXIT_LOAD 3
#define EXIT_EXPORT 4
#define EXIT_SELECTION 5
#define EXIT_WRITE 6
#define EXIT_CAPABILITY 7

#define FAIL_OFFSET (-1)
#define FAIL_OVERRUN (-2)
#define FAIL_TOTAL (-3)
#define FAIL_OPTION (-4)
#define FAIL_NOT_REGULAR (-5)
#define FAIL_SELECTION_LEFT (-6)

#define MAX_SELECTED 4000000u
#define MAX_SELECTION_BYTES (MAX_SELECTED * 21u)

static HANDLE g_in;
static HANDLE g_out;
static int g_outFailed;
static unsigned char g_inBuf[1 << 16];
static char g_outBuf[1 << 16];
static DWORD g_outLen;

static void flush_out(void)
{
    DWORD off = 0;
    while (!g_outFailed && off < g_outLen) {
        DWORD n = 0;
        if (!WriteFile(g_out, g_outBuf + off, g_outLen - off, &n, NULL) || n == 0)
            g_outFailed = 1;
        off += n;
    }
    g_outLen = 0;
}

static void put(const char *s, DWORD len)
{
    while (len > 0) {
        const DWORD room = (DWORD)sizeof g_outBuf - g_outLen;
        const DWORD k = len < room ? len : room;
        CopyMemory(g_outBuf + g_outLen, s, k);
        g_outLen += k;
        s += k;
        len -= k;
        if (g_outLen >= (DWORD)sizeof g_outBuf)
            flush_out();
    }
}

static void put_str(const char *s) { put(s, (DWORD)lstrlenA(s)); }

static const char HEX[] = "0123456789ABCDEF";

static void put_u64(unsigned __int64 v)
{
    char t[24];
    int i = (int)sizeof t;
    do {
        t[--i] = (char)('0' + (int)(v % 10));
        v /= 10;
    } while (v != 0 && i > 0);
    put(t + i, (DWORD)((int)sizeof t - i));
}

static void put_i64(__int64 v)
{
    if (v < 0) {
        put("-", 1);
        put_u64((unsigned __int64)0 - (unsigned __int64)v);
    } else {
        put_u64((unsigned __int64)v);
    }
}

/* "-" for NULL, "." for empty, else two hex digits per byte. */
static void put_hex8(const char *s)
{
    if (s == NULL) { put("-", 1); return; }
    if (*s == 0) { put(".", 1); return; }
    for (; *s != 0; s++) {
        const unsigned char c = (unsigned char)*s;
        const char t[2] = { HEX[c >> 4], HEX[c & 15] };
        put(t, 2);
    }
}

/* "-" for NULL, "." for empty, else four hex digits per UTF-16 code unit. */
static void put_hex16(const wchar_t *s)
{
    if (s == NULL) { put("-", 1); return; }
    if (*s == 0) { put(".", 1); return; }
    for (; *s != 0; s++) {
        const unsigned v = (unsigned short)*s;
        const char t[4] = { HEX[(v >> 12) & 15], HEX[(v >> 8) & 15], HEX[(v >> 4) & 15], HEX[v & 15] };
        put(t, 4);
    }
}

static la_ssize_t __cdecl on_read(void *a, void *d, const void **block)
{
    DWORD n = 0;
    (void)a;
    (void)d;
    if (!ReadFile(g_in, g_inBuf, (DWORD)sizeof g_inBuf, &n, NULL))
        return -1;
    *block = g_inBuf;
    return (la_ssize_t)n;
}

static la_int64_t __cdecl on_seek(void *a, void *d, la_int64_t offset, int whence)
{
    LARGE_INTEGER o;
    LARGE_INTEGER r;
    (void)a;
    (void)d;
    if (whence < 0 || whence > 2)
        return -30;
    o.QuadPart = offset;
    if (!SetFilePointerEx(g_in, o, &r, (DWORD)whence))
        return -30;
    return r.QuadPart;
}

static struct {
    const char *(__cdecl *version_details)(void);
    void *(__cdecl *read_new)(void);
    int(__cdecl *set_options)(void *, const char *);
    int(__cdecl *set_seek_callback)(void *, seek_cb);
    int(__cdecl *read_open)(void *, void *, void *, read_cb, void *);
    int(__cdecl *next_header)(void *, void **);
    int(__cdecl *data_block)(void *, const void **, size_t *, la_int64_t *);
    int(__cdecl *data_skip)(void *);
    const char *(__cdecl *error_string)(void *);
    int(__cdecl *errno_)(void *);
    int(__cdecl *read_free)(void *);
    const wchar_t *(__cdecl *pathname_w)(void *);
    const wchar_t *(__cdecl *symlink_w)(void *);
    const wchar_t *(__cdecl *hardlink_w)(void *);
    unsigned(__cdecl *filetype)(void *);
    la_int64_t(__cdecl *size)(void *);
    int(__cdecl *size_is_set)(void *);
    __int64(__cdecl *mtime)(void *);
    long(__cdecl *mtime_nsec)(void *);
    int(__cdecl *mtime_is_set)(void *);
    int(__cdecl *is_encrypted)(void *);
    int(__cdecl *sparse_count)(void *);
} la;

/* The formats and filters Pakko may name. Never support_*_all: that adds mtree, which reads
   local files, and filters that start an external program (DECISIONS, "T-F470"). */
static struct support {
    const char *name;
    const char *exportName;
    support_fn fn;
} g_supports[] = {
    { "tar", "archive_read_support_format_tar", NULL },
    { "cpio", "archive_read_support_format_cpio", NULL },
    { "7zip", "archive_read_support_format_7zip", NULL },
    { "rar", "archive_read_support_format_rar", NULL },
    { "rar5", "archive_read_support_format_rar5", NULL },
    { "cab", "archive_read_support_format_cab", NULL },
    { "gzip", "archive_read_support_filter_gzip", NULL },
    { "bzip2", "archive_read_support_filter_bzip2", NULL },
    { "compress", "archive_read_support_filter_compress", NULL },
    { "xz", "archive_read_support_filter_xz", NULL },
    { "lzma", "archive_read_support_filter_lzma", NULL },
    { "lzip", "archive_read_support_filter_lzip", NULL },
    { "zstd", "archive_read_support_filter_zstd", NULL },
};
#define SUPPORT_COUNT ((int)(sizeof g_supports / sizeof g_supports[0]))

static int load(void)
{
    int missing = 0;
    int i;
    const HMODULE m = LoadLibraryExW(L"C:\\Windows\\System32\\archiveint.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (m == NULL)
        return EXIT_LOAD;
#define BIND(field, name)                                       \
    *(FARPROC *)&la.field = GetProcAddress(m, name);            \
    if (la.field == NULL) {                                     \
        put_str("X ");                                          \
        put_str(name);                                          \
        put_str("\n");                                          \
        missing = 1;                                            \
    }
    BIND(version_details, "archive_version_details")
    BIND(read_new, "archive_read_new")
    BIND(set_options, "archive_read_set_options")
    BIND(set_seek_callback, "archive_read_set_seek_callback")
    BIND(read_open, "archive_read_open")
    BIND(next_header, "archive_read_next_header")
    BIND(data_block, "archive_read_data_block")
    BIND(data_skip, "archive_read_data_skip")
    BIND(error_string, "archive_error_string")
    BIND(errno_, "archive_errno")
    BIND(read_free, "archive_read_free")
    BIND(pathname_w, "archive_entry_pathname_w")
    BIND(symlink_w, "archive_entry_symlink_w")
    BIND(hardlink_w, "archive_entry_hardlink_w")
    BIND(filetype, "archive_entry_filetype")
    BIND(size, "archive_entry_size")
    BIND(size_is_set, "archive_entry_size_is_set")
    BIND(mtime, "archive_entry_mtime")
    BIND(mtime_nsec, "archive_entry_mtime_nsec")
    BIND(mtime_is_set, "archive_entry_mtime_is_set")
    BIND(is_encrypted, "archive_entry_is_encrypted")
    BIND(sparse_count, "archive_entry_sparse_count")
#undef BIND
    /* A build without one format or filter still serves the others: probe reports it as "-",
       and only a run that asks for it is refused. */
    for (i = 0; i < SUPPORT_COUNT; i++)
        g_supports[i].fn = (support_fn)(void *)GetProcAddress(m, g_supports[i].exportName);
    return missing ? EXIT_EXPORT : EXIT_DONE;
}

static int report_failure(void *ar, __int64 code)
{
    put_str("F ");
    put_i64(code);
    put_str(" ");
    put_hex8(ar != NULL ? la.error_string(ar) : NULL);
    put_str("\n");
    return EXIT_ARCHIVE;
}

static int report_own_failure(__int64 code)
{
    put_str("F ");
    put_i64(code);
    put_str(" .\n");
    return EXIT_ARCHIVE;
}

/* The selection: ASCII decimal indices, one per line ending in '\n', strictly increasing, no
   leading zeros. Anything else refuses the run. */
static int read_selection(const char *path, unsigned __int64 **selected, DWORD *count)
{
    int result = EXIT_SELECTION;
    unsigned char *text = NULL;
    unsigned __int64 *indices = NULL;
    DWORD got = 0;
    DWORD n = 0;
    DWORD i;
    DWORD capacity;
    unsigned __int64 current = 0;
    int digits = 0;
    LARGE_INTEGER size;
    const HANDLE h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
                                 FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (h == INVALID_HANDLE_VALUE)
        return EXIT_SELECTION;
    if (!GetFileSizeEx(h, &size) || size.QuadPart <= 0 || size.QuadPart > (LONGLONG)MAX_SELECTION_BYTES)
        goto done;
    text = (unsigned char *)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)size.QuadPart);
    if (text == NULL)
        goto done;
    if (!ReadFile(h, text, (DWORD)size.QuadPart, &got, NULL) || got != (DWORD)size.QuadPart)
        goto done;
    capacity = got / 2 + 1;
    indices = (unsigned __int64 *)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)capacity * sizeof *indices);
    if (indices == NULL)
        goto done;
    for (i = 0; i < got; i++) {
        const unsigned char c = text[i];
        if (c >= '0' && c <= '9') {
            if (digits >= 18 || (digits >= 1 && current == 0))
                goto done;
            current = current * 10 + (unsigned)(c - '0');
            digits++;
        } else if (c == '\n') {
            if (digits == 0 || n >= capacity || n >= MAX_SELECTED)
                goto done;
            if (n > 0 && current <= indices[n - 1])
                goto done;
            indices[n++] = current;
            current = 0;
            digits = 0;
        } else {
            goto done;
        }
    }
    if (digits != 0 || n == 0)
        goto done;
    *selected = indices;
    *count = n;
    indices = NULL;
    result = EXIT_DONE;
done:
    if (indices != NULL)
        HeapFree(GetProcessHeap(), 0, indices);
    if (text != NULL)
        HeapFree(GetProcessHeap(), 0, text);
    CloseHandle(h);
    return result;
}

static void put_entry(unsigned __int64 index, void *e)
{
    const int encrypted = la.is_encrypted(e);
    const int hasTime = la.mtime_is_set(e);
    const wchar_t *link = la.symlink_w(e);
    if (link == NULL)
        link = la.hardlink_w(e);
    put_str("E ");
    put_u64(index);
    put_str(" ");
    put_u64(la.filetype(e));
    put_str(" ");
    if (la.size_is_set(e))
        put_i64(la.size(e));
    else
        put("-", 1);
    put_str(" ");
    if (hasTime)
        put_i64(la.mtime(e));
    else
        put("-", 1);
    put_str(" ");
    put_i64(hasTime ? la.mtime_nsec(e) : 0);
    put_str(" ");
    if (encrypted < 0)
        put("-", 1);
    else
        put_u64((unsigned)encrypted);
    put_str(" ");
    put_i64(la.sparse_count(e));
    put_str(" ");
    put_hex16(la.pathname_w(e));
    put_str(" ");
    put_hex16(link);
    put_str("\n");
}

static int is_plain_file(void *e)
{
    return la.filetype(e) == AE_IFREG && la.symlink_w(e) == NULL && la.hardlink_w(e) == NULL;
}

static void to_filetime(__int64 seconds, long nanoseconds, FILETIME *ft)
{
    ULARGE_INTEGER u;
    u.QuadPart = (unsigned __int64)(seconds + 11644473600LL) * 10000000ULL + (unsigned __int64)nanoseconds / 100;
    ft->dwLowDateTime = u.LowPart;
    ft->dwHighDateTime = u.HighPart;
}

static int open_output(unsigned __int64 index, HANDLE *file)
{
    wchar_t path[32] = L"out\\";
    wchar_t digits[24];
    int i = 24;
    int k = 4;
    do {
        digits[--i] = (wchar_t)(L'0' + (int)(index % 10));
        index /= 10;
    } while (index != 0 && i > 0);
    while (i < 24 && k < 31)
        path[k++] = digits[i++];
    path[k] = 0;
    *file = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    return *file == INVALID_HANDLE_VALUE ? EXIT_WRITE : EXIT_DONE;
}

static int write_at(HANDLE file, la_int64_t offset, const void *block, size_t length)
{
    LARGE_INTEGER position;
    const unsigned char *p = (const unsigned char *)block;
    position.QuadPart = offset;
    if (!SetFilePointerEx(file, position, NULL, FILE_BEGIN))
        return EXIT_WRITE;
    while (length > 0) {
        const DWORD chunk = length > 0x40000000u ? 0x40000000u : (DWORD)length;
        DWORD n = 0;
        if (!WriteFile(file, p, chunk, &n, NULL) || n == 0)
            return EXIT_WRITE;
        p += n;
        length -= n;
    }
    return EXIT_DONE;
}

/* Reads one entry's data; writes it to out\<index> when file is not INVALID_HANDLE_VALUE. The
   total is charged with the entry's logical end, not its data bytes: a hole costs the disk what data
   does (NTFS zero-fills up to the end), so a sparse entry claiming terabytes is stopped before the
   write that would reach them. */
static int copy_entry(void *ar, void *e, HANDLE file, unsigned __int64 *total, unsigned __int64 maxTotal, unsigned __int64 *bytes)
{
    const la_int64_t declared = la.size_is_set(e) ? la.size(e) : -1;
    const unsigned __int64 room = maxTotal >= *total ? maxTotal - *total : 0;
    unsigned __int64 end = 0;
    *bytes = 0;
    for (;;) {
        const void *block = NULL;
        size_t length = 0;
        la_int64_t offset = 0;
        const int r = la.data_block(ar, &block, &length, &offset);
        if (r == ARCHIVE_EOF)
            break;
        if (r < ARCHIVE_WARN)
            return report_failure(ar, r);
        if (offset < 0 || (unsigned __int64)offset + length < (unsigned __int64)offset)
            return report_own_failure(FAIL_OFFSET);
        if ((unsigned __int64)offset + length > end)
            end = (unsigned __int64)offset + length;
        if (declared >= 0 && end > (unsigned __int64)declared)
            return report_own_failure(FAIL_OVERRUN);
        if (end > room)
            return report_own_failure(FAIL_TOTAL);
        if (file != INVALID_HANDLE_VALUE && length > 0 && write_at(file, offset, block, length) != EXIT_DONE)
            return EXIT_WRITE;
    }
    /* Only a sparse entry ends in a hole; a plain one keeps the length its data gave, which Pakko
       compares with the listed size. */
    if (la.sparse_count(e) > 0 && declared >= 0 && end < (unsigned __int64)declared)
        end = (unsigned __int64)declared;
    if (end > room)
        return report_own_failure(FAIL_TOTAL);
    *total += end;
    *bytes = end;
    if (file != INVALID_HANDLE_VALUE) {
        LARGE_INTEGER position;
        position.QuadPart = (LONGLONG)end;
        if (!SetFilePointerEx(file, position, NULL, FILE_BEGIN) || !SetEndOfFile(file))
            return EXIT_WRITE;
        if (la.mtime_is_set(e)) {
            FILETIME ft;
            to_filetime(la.mtime(e), la.mtime_nsec(e), &ft);
            if (!SetFileTime(file, NULL, NULL, &ft))
                return EXIT_WRITE;
        }
    }
    return EXIT_DONE;
}

static int extract_one(void *ar, void *e, unsigned __int64 index, int write, unsigned __int64 *total, unsigned __int64 maxTotal)
{
    HANDLE file = INVALID_HANDLE_VALUE;
    unsigned __int64 bytes = 0;
    int r;
    if (!is_plain_file(e))
        return report_own_failure(FAIL_NOT_REGULAR);
    if (write && open_output(index, &file) != EXIT_DONE)
        return EXIT_WRITE;
    r = copy_entry(ar, e, file, total, maxTotal, &bytes);
    if (file != INVALID_HANDLE_VALUE && !CloseHandle(file) && r == EXIT_DONE)
        r = EXIT_WRITE;
    if (r == EXIT_DONE) {
        put_str("K ");
        put_u64(index);
        put_str(" ");
        put_u64(bytes);
        put_str("\n");
    }
    return r;
}

static int parse_u64(const char *s, unsigned __int64 *value)
{
    unsigned __int64 x = 0;
    int n = 0;
    for (; *s != 0; s++, n++) {
        if (*s < '0' || *s > '9' || n >= 19)
            return 0;
        x = x * 10 + (unsigned)(*s - '0');
    }
    *value = x;
    return n > 0;
}

/* Marks each comma-separated name of list in wanted; an unknown or repeated name is a usage error. */
static int parse_supports(const char *list, int *wanted)
{
    while (*list != 0) {
        int len = 0;
        int i;
        int found = 0;
        while (list[len] != 0 && list[len] != ',')
            len++;
        for (i = 0; i < SUPPORT_COUNT; i++) {
            if (lstrlenA(g_supports[i].name) == len) {
                int k = 0;
                while (k < len && g_supports[i].name[k] == list[k])
                    k++;
                if (k == len) {
                    if (wanted[i])
                        return 0;
                    wanted[i] = 1;
                    found = 1;
                }
            }
        }
        if (!found || len == 0)
            return 0;
        list += len;
        if (*list == ',') {
            list++;
            if (*list == 0)
                return 0;
        }
    }
    return 1;
}

static int valid_charset(const char *s)
{
    int n = 0;
    for (; *s != 0; s++, n++) {
        const char c = *s;
        if (n >= 16 || !((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '-'))
            return 0;
    }
    return n > 0;
}

static int probe(void)
{
    int i;
    for (i = 0; i < SUPPORT_COUNT; i++) {
        void *ar = la.read_new();
        if (ar == NULL)
            return EXIT_ARCHIVE;
        put_str("C ");
        put_str(g_supports[i].name);
        put_str(" ");
        if (g_supports[i].fn == NULL)
            put("-", 1);
        else
            put_i64(g_supports[i].fn(ar));
        put_str("\n");
        la.read_free(ar);
    }
    return EXIT_DONE;
}

static int run(void *ar, int extract, int test, const unsigned __int64 *selected, DWORD selectedCount, unsigned __int64 maxTotal)
{
    unsigned __int64 index;
    unsigned __int64 total = 0;
    DWORD next = 0;
    int r = la.read_open(ar, NULL, NULL, on_read, NULL);
    if (r != ARCHIVE_OK)
        return report_failure(ar, r);
    for (index = 0;; index++) {
        void *e = NULL;
        r = la.next_header(ar, &e);
        if (r == ARCHIVE_EOF)
            break;
        if (r < ARCHIVE_WARN)
            return report_failure(ar, r);
        if (r == ARCHIVE_WARN) {
            put_str("W ");
            put_u64(index);
            put_str(" ");
            put_i64(la.errno_(ar));
            put_str(" ");
            put_hex8(la.error_string(ar));
            put_str("\n");
        }
        put_entry(index, e);
        if ((extract || test) && next < selectedCount && selected[next] == index) {
            next++;
            r = extract_one(ar, e, index, extract, &total, maxTotal);
            if (r != EXIT_DONE)
                return r;
        } else {
            r = la.data_skip(ar);
            if (r < ARCHIVE_WARN)
                return report_failure(ar, r);
        }
    }
    if (next != selectedCount) {
        report_own_failure(FAIL_SELECTION_LEFT);
        return EXIT_SELECTION;
    }
    return EXIT_DONE;
}

int __cdecl main(int argc, char **argv)
{
    const char *mode;
    const char *charset = NULL;
    const char *selectPath = NULL;
    const char *formats = NULL;
    const char *filters = NULL;
    unsigned __int64 maxTotal = ~0ULL;
    unsigned __int64 *selected = NULL;
    DWORD selectedCount = 0;
    int haveMax = 0;
    int isProbe;
    int extract;
    int test;
    int wanted[SUPPORT_COUNT] = { 0 };
    int result = EXIT_USAGE;
    int i;
    void *ar = NULL;

    g_in = GetStdHandle(STD_INPUT_HANDLE);
    g_out = GetStdHandle(STD_OUTPUT_HANDLE);
    if (argc < 2)
        return EXIT_USAGE;
    mode = argv[1];
    for (i = 2; i < argc; i++) {
        if (i + 1 >= argc)
            return EXIT_USAGE;
        if (lstrcmpA(argv[i], "--hdrcharset") == 0 && charset == NULL)
            charset = argv[++i];
        else if (lstrcmpA(argv[i], "--select") == 0 && selectPath == NULL)
            selectPath = argv[++i];
        else if (lstrcmpA(argv[i], "--format") == 0 && formats == NULL)
            formats = argv[++i];
        else if (lstrcmpA(argv[i], "--filter") == 0 && filters == NULL)
            filters = argv[++i];
        else if (lstrcmpA(argv[i], "--max-total") == 0 && !haveMax) {
            if (!parse_u64(argv[++i], &maxTotal))
                return EXIT_USAGE;
            haveMax = 1;
        } else {
            return EXIT_USAGE;
        }
    }
    isProbe = lstrcmpA(mode, "probe") == 0;
    extract = lstrcmpA(mode, "extract") == 0;
    test = lstrcmpA(mode, "test") == 0;
    if (isProbe) {
        if (argc != 2)
            return EXIT_USAGE;
    } else {
        if (lstrcmpA(mode, "list") != 0 && !extract && !test)
            return EXIT_USAGE;
        if (formats == NULL || formats[0] == 0 || !parse_supports(formats, wanted))
            return EXIT_USAGE;
        if (filters != NULL && !parse_supports(filters, wanted))
            return EXIT_USAGE;
        if ((extract || test) != (selectPath != NULL) || extract != haveMax)
            return EXIT_USAGE;
        if (charset != NULL && !valid_charset(charset))
            return EXIT_USAGE;
    }

    result = load();
    if (result == EXIT_LOAD)
        goto done;
    put_str("V ");
    put_hex8(result == EXIT_DONE ? la.version_details() : NULL);
    put_str("\n");
    if (result != EXIT_DONE)
        goto done;
    if (isProbe) {
        result = probe();
        if (result == EXIT_DONE)
            put_str("D\n");
        goto done;
    }
    if (selectPath != NULL) {
        result = read_selection(selectPath, &selected, &selectedCount);
        if (result != EXIT_DONE)
            goto done;
    }

    ar = la.read_new();
    if (ar == NULL) {
        result = EXIT_ARCHIVE;
        goto done;
    }
    for (i = 0; i < SUPPORT_COUNT; i++) {
        if (wanted[i] && (g_supports[i].fn == NULL || g_supports[i].fn(ar) != ARCHIVE_OK)) {
            put_str("F 0 .\n");
            result = EXIT_CAPABILITY;
            goto done;
        }
    }
    if (charset != NULL) {
        char option[32] = "hdrcharset=";
        lstrcatA(option, charset);
        if (la.set_options(ar, option) != ARCHIVE_OK) {
            result = report_failure(ar, FAIL_OPTION);
            goto done;
        }
    }
    la.set_seek_callback(ar, on_seek);
    result = run(ar, extract, test, selected, selectedCount, maxTotal);
    if (result == EXIT_DONE)
        put_str("D\n");

done:
    if (ar != NULL)
        la.read_free(ar);
    if (selected != NULL)
        HeapFree(GetProcessHeap(), 0, selected);
    flush_out();
    if (g_outFailed && result == EXIT_DONE)
        result = EXIT_WRITE;
    return result;
}
