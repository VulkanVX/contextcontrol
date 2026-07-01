#include <algorithm>
#include <array>
#include <cctype>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <regex>
#include <set>
#include <sstream>
#include <string>
#include <tuple>
#include <unordered_set>
#include <vector>

namespace fs = std::filesystem;

namespace {

struct FileInfo {
    fs::path fullPath;
    std::string relSlash;
    std::string relNative;
    std::string name;
    std::string ext;
    std::string kind;
    std::uintmax_t size = 0;
};

struct FunctionRange {
    std::string name;
    int start = 0;
    int end = 0;
    std::string hash;
};

struct FamilyRecord {
    std::string path;
    std::string role;
    std::string exports;
    int score = 0;
};

struct Options {
    std::string outputFile;
    int lod = 0;
    std::string scope;
    int maxDepth = 20;
    std::string profile = "auto";
    bool profileOnly = false;
    std::string profileOutput;
    std::string profileFile;
    int maxFileKB = 512;
    bool forceLargeFiles = false;
    bool includeArtifacts = false;
    bool includeAllTopLevel = false;
    bool noClipboard = false;
    bool hashHints = false;
};

struct FileRules {
    std::set<std::string> ignoredDirectories;
    std::set<std::string> ignoredExtensions;
    std::set<std::string> ignoredFileNames;
    std::set<std::string> supportedExtensions;
    std::vector<std::string> dirFingerprintIgnoredDirectories;
    std::vector<std::string> dirFingerprintIgnoredFileNames;
    std::vector<std::string> dirFingerprintIgnoredExtensions;
};

const std::vector<std::string> DirIgnoredDirectories = {
    ".git", ".vs", ".vscode", ".idea", ".cache", ".godot", ".import", "node_modules",
    "dist", "build", "build-debug", "build-release", "cmake-build-debug",
    "cmake-build-release", "CMakeFiles", "out", "bin", "obj", "x64", "Debug", "Release",
    "RelWithDebInfo", "MinSizeRel", "vcpkg_installed", "packages", "PackageCache",
    "external", "extern", "third_party", "thirdparty", "vendor", "deps", "dependencies",
    "__pycache__"
};

const std::vector<std::string> DirIgnoredExtensions = {
    ".import", ".uid", ".tmp", ".log", ".bak", ".pdb", ".ilk", ".obj", ".o", ".lib",
    ".dll", ".exe", ".exp", ".spv", ".cache", ".db", ".opendb", ".sdf", ".ipch",
    ".tlog", ".lastbuildstate", ".unsuccessfulbuild", ".png", ".jpg", ".jpeg", ".webp",
    ".bmp", ".tga", ".dds", ".wav", ".mp3", ".ogg", ".flac", ".bin", ".collision",
    ".svo"
};

const std::vector<std::string> DirArtifactIgnoredDirectories = {
    ".contextcontrol", ".ccReplace.versions", ".ccWorkbench.browser-data",
    ".ccWorkbench.generated-projects", ".gptReplace.versions", ".tmp", ".tmp-build",
    "codex-harness", "dotnet-obj", "dotnet-out", "generated", "ps1_nat",
    "ps1_nat_gist_review", "test-harness", "test-results"
};

const std::vector<std::string> DirArtifactIgnoredFileNames = {
    ".ccWorkbench.chat-history*.json", "cc_chat_export_*.md", "cc_code_export.md",
    ".ccDirProfile.json", "cc_project_dir.md", "cc_semantic_map.md", "cleanup-*.ps1",
    "mainwindow_axaml.txt", "nat-*.ps1", "*-nat-*.ps1", "*.generated.json",
    "*.generated.md", "*.nat.*", "*.tmp.md", "*.tmp.txt", "*_nat_*", "*bug-hunt*",
    "*bughunt*", "patch.txt"
};

const std::set<std::string> DirManifestArtifactParts = {
    ".ccreplace.versions", ".ccworkbench.browser-data", ".ccworkbench.generated-projects",
    ".gptreplace.versions", ".git", ".idea", ".tmp", ".tmp-build", ".vs", ".vscode",
    "__pycache__", "artifact", "artifacts", "bin", "build", "cache", "cmakefiles",
    "codex-harness", "debug", "deps", "dependencies", "dist", "dotnet-obj", "dotnet-out",
    "external", "extern", "generated", "history", "node_modules", "obj", "out", "packages",
    "packagecache", "ps1_nat", "release", "test-harness", "test-results", "third_party",
    "thirdparty", "vendor", "vcpkg_installed", "x64"
};

const std::vector<std::string> DefaultIgnoredDirectories = {
    ".contextcontrol", ".ccReplace.versions", ".ccWorkbench.browser-data", ".ccWorkbench.generated-projects",
    ".angular", ".claude", ".conan", ".conan2", ".codex", ".cursor", ".dart_tool",
    ".expo", ".git", ".godot", ".gptReplace.versions", ".gradle", ".idea", ".import",
    ".m2", ".next", ".nuxt", ".nuget", ".parcel-cache", ".pnpm-store", ".serverless",
    ".svelte-kit", ".terraform", ".tmp", ".tmp-build", ".turbo", ".venv", ".vs",
    ".vscode", ".yarn", "__pycache__", "_build", "_deps", "bin", "build",
    "build-debug", "build-release", "cmake-build-debug", "cmake-build-release",
    "CMakeFiles", "codex-harness", "coverage", "DerivedData", "deps", "dist",
    "dotnet-obj", "dotnet-out", "external", "extern", "generated", "node_modules",
    "Debug", "MinSizeRel", "Release", "RelWithDebInfo", "PackageCache", "dependencies",
    "obj", "out", "packages", "Pods", "ps1_nat", "ps1_nat_gist_review", "test-harness",
    "test-results", "third_party", "thirdparty", "vendor", "venv", "vcpkg_installed", "x64"
};

const std::vector<std::string> DefaultIgnoredExtensions = {
    ".bak", ".bin", ".bmp", ".cache", ".collision", ".db", ".dds", ".dll", ".exe",
    ".exp", ".flac", ".ilk", ".import", ".ipch", ".jpg", ".jpeg", ".lastbuildstate",
    ".lib", ".log", ".mp3", ".o", ".obj", ".ogg", ".opendb", ".pdb", ".png", ".sdf",
    ".snapshot", ".spv", ".svo", ".tga", ".tlog", ".tmp", ".uid", ".unsuccessfulbuild",
    ".wav", ".webp"
};

const std::vector<std::string> DefaultIgnoredFileNames = {
    ".ccDirProfile.json", ".ccFileRules.json", ".ccReplace.settings*.json",
    ".ccWorkbench.chat-history*.json", ".ccWorkbench.settings.json",
    ".DS_Store", "*.generated.json", "*.generated.md", "*.nat.*", "*.tmp.md", "*.tmp.txt",
    "*-nat-*.ps1", "*_nat_*", "*bug-hunt*", "*bughunt*", "cc_chat_export_*.md",
    "cc_code_export.md", "cc_project_dir.md", "cc_semantic_map.md", "cleanup-*.ps1",
    "desktop.ini", "mainwindow_axaml.txt", "nat-*.ps1", "patch.txt", "Thumbs.db"
};

const std::vector<std::string> DefaultSupportedExtensions = {
    ".axaml", ".bat", ".c", ".cc", ".cmd", ".comp", ".cpp", ".cs", ".csproj", ".css",
    ".cxx", ".frag", ".fs", ".fsproj", ".geom", ".glsl", ".h", ".hh", ".hpp", ".html",
    ".hlsl", ".hxx", ".inc", ".ini", ".inl", ".ipp", ".go", ".gradle", ".java", ".js",
    ".json", ".jsx", ".kt", ".kts", ".lock", ".lua", ".m", ".md", ".mesh", ".metal",
    ".mm", ".mod", ".props", ".ps1", ".psd1", ".psm1", ".py", ".rs", ".shader", ".sh",
    ".slang", ".sum", ".targets", ".task", ".tesc", ".tese", ".toml", ".ts", ".tsx",
    ".txt", ".vert", ".wgsl", ".xaml", ".xml", ".yaml", ".yml"
};

std::string ToLower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

std::string Trim(const std::string& value) {
    std::size_t first = 0;
    if (value.size() >= 3 &&
        static_cast<unsigned char>(value[0]) == 0xEF &&
        static_cast<unsigned char>(value[1]) == 0xBB &&
        static_cast<unsigned char>(value[2]) == 0xBF) {
        first = 3;
    }

    while (first < value.size() && std::isspace(static_cast<unsigned char>(value[first]))) {
        ++first;
    }

    std::size_t last = value.size();
    while (last > first && std::isspace(static_cast<unsigned char>(value[last - 1]))) {
        --last;
    }

    return value.substr(first, last - first);
}

bool StartsWith(const std::string& value, const std::string& prefix) {
    return value.size() >= prefix.size() && value.compare(0, prefix.size(), prefix) == 0;
}

bool EndsWith(const std::string& value, const std::string& suffix) {
    return value.size() >= suffix.size() &&
        value.compare(value.size() - suffix.size(), suffix.size(), suffix) == 0;
}

bool ContainsCI(const std::string& value, const std::string& needle) {
    return ToLower(value).find(ToLower(needle)) != std::string::npos;
}

std::string ReplaceAll(std::string value, char from, char to) {
    std::replace(value.begin(), value.end(), from, to);
    return value;
}

std::string Slashes(std::string value) {
    return ReplaceAll(std::move(value), '\\', '/');
}

std::string NativeDisplay(std::string relSlash) {
#ifdef _WIN32
    return ReplaceAll(std::move(relSlash), '/', '\\');
#else
    return relSlash;
#endif
}

std::string EscapeJson(const std::string& value) {
    std::string result;
    result.reserve(value.size() + 8);
    for (char ch : value) {
        switch (ch) {
        case '\\':
            result += "\\\\";
            break;
        case '"':
            result += "\\\"";
            break;
        case '\n':
            result += "\\n";
            break;
        case '\r':
            result += "\\r";
            break;
        case '\t':
            result += "\\t";
            break;
        default:
            result += ch;
            break;
        }
    }
    return result;
}

std::string ReadFile(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) {
        throw std::runtime_error("Could not read file: " + path.string());
    }

    std::ostringstream buffer;
    buffer << stream.rdbuf();
    return buffer.str();
}

void WriteFile(const fs::path& path, const std::string& text) {
    if (path.has_parent_path()) {
        fs::create_directories(path.parent_path());
    }

    std::ofstream stream(path, std::ios::binary);
    if (!stream) {
        throw std::runtime_error("Could not write file: " + path.string());
    }

    stream << text;
}

std::string HexBytes(const std::array<std::uint8_t, 32>& bytes) {
    std::ostringstream out;
    out << std::hex << std::setfill('0');
    for (auto byte : bytes) {
        out << std::setw(2) << static_cast<int>(byte);
    }
    return out.str();
}

std::uint32_t RotateRight(std::uint32_t value, std::uint32_t bits) {
    return (value >> bits) | (value << (32 - bits));
}

std::array<std::uint8_t, 32> Sha256Bytes(const std::vector<std::uint8_t>& input) {
    static constexpr std::array<std::uint32_t, 64> k = {
        0x428a2f98U, 0x71374491U, 0xb5c0fbcfU, 0xe9b5dba5U, 0x3956c25bU, 0x59f111f1U, 0x923f82a4U, 0xab1c5ed5U,
        0xd807aa98U, 0x12835b01U, 0x243185beU, 0x550c7dc3U, 0x72be5d74U, 0x80deb1feU, 0x9bdc06a7U, 0xc19bf174U,
        0xe49b69c1U, 0xefbe4786U, 0x0fc19dc6U, 0x240ca1ccU, 0x2de92c6fU, 0x4a7484aaU, 0x5cb0a9dcU, 0x76f988daU,
        0x983e5152U, 0xa831c66dU, 0xb00327c8U, 0xbf597fc7U, 0xc6e00bf3U, 0xd5a79147U, 0x06ca6351U, 0x14292967U,
        0x27b70a85U, 0x2e1b2138U, 0x4d2c6dfcU, 0x53380d13U, 0x650a7354U, 0x766a0abbU, 0x81c2c92eU, 0x92722c85U,
        0xa2bfe8a1U, 0xa81a664bU, 0xc24b8b70U, 0xc76c51a3U, 0xd192e819U, 0xd6990624U, 0xf40e3585U, 0x106aa070U,
        0x19a4c116U, 0x1e376c08U, 0x2748774cU, 0x34b0bcb5U, 0x391c0cb3U, 0x4ed8aa4aU, 0x5b9cca4fU, 0x682e6ff3U,
        0x748f82eeU, 0x78a5636fU, 0x84c87814U, 0x8cc70208U, 0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U
    };

    std::vector<std::uint8_t> bytes = input;
    const std::uint64_t bitLength = static_cast<std::uint64_t>(bytes.size()) * 8U;
    bytes.push_back(0x80U);
    while ((bytes.size() % 64U) != 56U) {
        bytes.push_back(0U);
    }
    for (int shift = 56; shift >= 0; shift -= 8) {
        bytes.push_back(static_cast<std::uint8_t>((bitLength >> shift) & 0xffU));
    }

    std::uint32_t h0 = 0x6a09e667U;
    std::uint32_t h1 = 0xbb67ae85U;
    std::uint32_t h2 = 0x3c6ef372U;
    std::uint32_t h3 = 0xa54ff53aU;
    std::uint32_t h4 = 0x510e527fU;
    std::uint32_t h5 = 0x9b05688cU;
    std::uint32_t h6 = 0x1f83d9abU;
    std::uint32_t h7 = 0x5be0cd19U;

    for (std::size_t chunk = 0; chunk < bytes.size(); chunk += 64) {
        std::array<std::uint32_t, 64> w{};
        for (std::size_t i = 0; i < 16; ++i) {
            const std::size_t j = chunk + i * 4;
            w[i] = (static_cast<std::uint32_t>(bytes[j]) << 24) |
                (static_cast<std::uint32_t>(bytes[j + 1]) << 16) |
                (static_cast<std::uint32_t>(bytes[j + 2]) << 8) |
                static_cast<std::uint32_t>(bytes[j + 3]);
        }
        for (std::size_t i = 16; i < 64; ++i) {
            const auto s0 = RotateRight(w[i - 15], 7) ^ RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            const auto s1 = RotateRight(w[i - 2], 17) ^ RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        auto a = h0;
        auto b = h1;
        auto c = h2;
        auto d = h3;
        auto e = h4;
        auto f = h5;
        auto g = h6;
        auto h = h7;

        for (std::size_t i = 0; i < 64; ++i) {
            const auto s1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
            const auto ch = (e & f) ^ ((~e) & g);
            const auto temp1 = h + s1 + ch + k[i] + w[i];
            const auto s0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
            const auto maj = (a & b) ^ (a & c) ^ (b & c);
            const auto temp2 = s0 + maj;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        h0 += a;
        h1 += b;
        h2 += c;
        h3 += d;
        h4 += e;
        h5 += f;
        h6 += g;
        h7 += h;
    }

    std::array<std::uint8_t, 32> result{};
    const std::array<std::uint32_t, 8> words = { h0, h1, h2, h3, h4, h5, h6, h7 };
    for (std::size_t i = 0; i < words.size(); ++i) {
        result[i * 4] = static_cast<std::uint8_t>((words[i] >> 24) & 0xffU);
        result[i * 4 + 1] = static_cast<std::uint8_t>((words[i] >> 16) & 0xffU);
        result[i * 4 + 2] = static_cast<std::uint8_t>((words[i] >> 8) & 0xffU);
        result[i * 4 + 3] = static_cast<std::uint8_t>(words[i] & 0xffU);
    }
    return result;
}

std::string Sha256Text(const std::string& text) {
    return HexBytes(Sha256Bytes(std::vector<std::uint8_t>(text.begin(), text.end())));
}

std::string Sha256File(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) {
        return "";
    }
    std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
    return HexBytes(Sha256Bytes(bytes));
}

std::string HexSha1(const std::array<std::uint8_t, 20>& bytes) {
    std::ostringstream out;
    out << std::hex << std::setfill('0');
    for (auto byte : bytes) {
        out << std::setw(2) << static_cast<int>(byte);
    }
    return out.str();
}

std::array<std::uint8_t, 20> Sha1Bytes(const std::vector<std::uint8_t>& input) {
    std::vector<std::uint8_t> bytes = input;
    const std::uint64_t bitLength = static_cast<std::uint64_t>(bytes.size()) * 8U;
    bytes.push_back(0x80U);
    while ((bytes.size() % 64U) != 56U) {
        bytes.push_back(0U);
    }
    for (int shift = 56; shift >= 0; shift -= 8) {
        bytes.push_back(static_cast<std::uint8_t>((bitLength >> shift) & 0xffU));
    }

    std::uint32_t h0 = 0x67452301U;
    std::uint32_t h1 = 0xefcdab89U;
    std::uint32_t h2 = 0x98badcfeU;
    std::uint32_t h3 = 0x10325476U;
    std::uint32_t h4 = 0xc3d2e1f0U;

    for (std::size_t chunk = 0; chunk < bytes.size(); chunk += 64) {
        std::array<std::uint32_t, 80> w{};
        for (std::size_t i = 0; i < 16; ++i) {
            const std::size_t j = chunk + i * 4;
            w[i] = (static_cast<std::uint32_t>(bytes[j]) << 24) |
                (static_cast<std::uint32_t>(bytes[j + 1]) << 16) |
                (static_cast<std::uint32_t>(bytes[j + 2]) << 8) |
                static_cast<std::uint32_t>(bytes[j + 3]);
        }
        for (std::size_t i = 16; i < 80; ++i) {
            const auto value = w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16];
            w[i] = (value << 1) | (value >> 31);
        }

        auto a = h0;
        auto b = h1;
        auto c = h2;
        auto d = h3;
        auto e = h4;

        for (std::size_t i = 0; i < 80; ++i) {
            std::uint32_t f = 0;
            std::uint32_t k = 0;
            if (i < 20) {
                f = (b & c) | ((~b) & d);
                k = 0x5a827999U;
            }
            else if (i < 40) {
                f = b ^ c ^ d;
                k = 0x6ed9eba1U;
            }
            else if (i < 60) {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8f1bbcdcU;
            }
            else {
                f = b ^ c ^ d;
                k = 0xca62c1d6U;
            }

            const auto temp = ((a << 5) | (a >> 27)) + f + e + k + w[i];
            e = d;
            d = c;
            c = (b << 30) | (b >> 2);
            b = a;
            a = temp;
        }

        h0 += a;
        h1 += b;
        h2 += c;
        h3 += d;
        h4 += e;
    }

    std::array<std::uint8_t, 20> result{};
    const std::array<std::uint32_t, 5> words = { h0, h1, h2, h3, h4 };
    for (std::size_t i = 0; i < words.size(); ++i) {
        result[i * 4] = static_cast<std::uint8_t>((words[i] >> 24) & 0xffU);
        result[i * 4 + 1] = static_cast<std::uint8_t>((words[i] >> 16) & 0xffU);
        result[i * 4 + 2] = static_cast<std::uint8_t>((words[i] >> 8) & 0xffU);
        result[i * 4 + 3] = static_cast<std::uint8_t>(words[i] & 0xffU);
    }
    return result;
}

std::string Sha1Short(const std::string& text) {
    const auto hex = HexSha1(Sha1Bytes(std::vector<std::uint8_t>(text.begin(), text.end())));
    return hex.substr(0, 8);
}

std::vector<std::string> SplitLines(const std::string& text) {
    std::vector<std::string> lines;
    std::string current;
    for (char ch : text) {
        if (ch == '\r') {
            continue;
        }
        if (ch == '\n') {
            lines.push_back(current);
            current.clear();
            continue;
        }
        current += ch;
    }
    lines.push_back(current);
    return lines;
}

fs::path ResolveProjectRoot() {
    if (const char* envRoot = std::getenv("CC_WORKBENCH_PROJECT_ROOT")) {
        if (std::string(envRoot).size() > 0) {
            return fs::absolute(fs::path(envRoot));
        }
    }

    return fs::current_path();
}

std::string GetEnvText(const char* name) {
    if (const char* value = std::getenv(name)) {
        return value;
    }
    return "";
}

std::string RelativeSlash(const fs::path& root, const fs::path& path) {
    std::error_code ec;
    auto rel = fs::relative(path, root, ec);
    if (ec) {
        rel = path;
    }

    return Slashes(rel.generic_string());
}

std::string ExtensionOf(const fs::path& path) {
    return ToLower(path.extension().generic_string());
}

std::string NormalizeRulePath(std::string value) {
    value = Slashes(Trim(std::move(value)));
    while (StartsWith(value, "./")) {
        value = value.substr(2);
    }
    while (!value.empty() && value.front() == '/') {
        value.erase(value.begin());
    }
    while (!value.empty() && value.back() == '/') {
        value.pop_back();
    }
    return ToLower(value);
}

std::string NormalizeDirFingerprintRulePath(std::string value) {
    value = Slashes(Trim(std::move(value)));
    while (StartsWith(value, "./")) {
        value = value.substr(2);
    }
    while (!value.empty() && value.front() == '/') {
        value.erase(value.begin());
    }
    while (!value.empty() && value.back() == '/') {
        value.pop_back();
    }
    return value;
}

std::string NormalizeExtensionRule(std::string value) {
    value = ToLower(Trim(std::move(value)));
    if (value.empty()) {
        return "";
    }
    return value.front() == '.' ? value : "." + value;
}

std::string LastPathPart(const std::string& relSlash) {
    const std::string normalized = NormalizeRulePath(relSlash);
    const auto pos = normalized.find_last_of('/');
    return pos == std::string::npos ? normalized : normalized.substr(pos + 1);
}

bool PathExists(const fs::path& path) {
    std::error_code ec;
    return fs::exists(path, ec);
}

std::string DetectDirProfile(const fs::path& root, const std::string& requestedProfile) {
    const auto clean = ToLower(Trim(requestedProfile.empty() ? "auto" : requestedProfile));
    if (clean != "auto") {
        return clean;
    }

    if (PathExists(root / "cc.ps1") &&
        PathExists(root / "ccDir.ps1") &&
        PathExists(root / "ccReplace.ps1") &&
        PathExists(root / "lib")) {
        return "contextcontrol";
    }

    if (PathExists(root / "CMakeLists.txt") &&
        PathExists(root / "src") &&
        PathExists(root / "include") &&
        PathExists(root / "shaders")) {
        return "vulkanvx";
    }

    if (PathExists(root / "project.godot") || PathExists(root / "scenes")) {
        return "godot";
    }

    return "generic";
}

bool ShouldIncludeTopLevelItem(const std::string& relSlash, bool includeAllTopLevel, const std::string& resolvedProfile) {
    if (includeAllTopLevel || resolvedProfile != "vulkanvx") {
        return true;
    }

    const auto relative = NormalizeRulePath(relSlash);
    if (relative.empty()) {
        return true;
    }

    const auto firstSlash = relative.find('/');
    const auto top = firstSlash == std::string::npos ? relative : relative.substr(0, firstSlash);
    static const std::set<std::string> allowed = {
        "include", "src", "shaders", "tools", "maps", "assets", "cmakelists.txt", "readme.md"
    };

    return allowed.count(relative) > 0 || allowed.count(top) > 0;
}

bool IsPathRule(const std::string& rule) {
    return rule.find('/') != std::string::npos;
}

std::string RegexEscape(const std::string& value) {
    std::string result;
    result.reserve(value.size() * 2);
    for (char ch : value) {
        switch (ch) {
        case '.':
        case '\\':
        case '+':
        case '^':
        case '$':
        case '(':
        case ')':
        case '[':
        case ']':
        case '{':
        case '}':
        case '|':
            result += '\\';
            result += ch;
            break;
        case '*':
            result += ".*";
            break;
        case '?':
            result += '.';
            break;
        default:
            result += ch;
            break;
        }
    }
    return result;
}

bool RuleMatchesText(const std::string& value, const std::string& rule) {
    if (value.empty() || rule.empty()) {
        return false;
    }
    if (rule.find('*') == std::string::npos && rule.find('?') == std::string::npos) {
        return ToLower(value) == ToLower(rule);
    }
    return std::regex_match(ToLower(value), std::regex("^" + RegexEscape(ToLower(rule)) + "$"));
}

bool PathMatchesDirectoryRule(const std::string& relativePath, const std::string& directoryRule) {
    const auto path = NormalizeRulePath(relativePath);
    const auto rule = NormalizeRulePath(directoryRule);
    return path == rule || StartsWith(path, rule + "/");
}

std::set<std::string> NormalizeNameSet(const std::vector<std::string>& values) {
    std::set<std::string> result;
    for (const auto& value : values) {
        const auto normalized = NormalizeRulePath(value);
        if (!normalized.empty()) {
            result.insert(normalized);
        }
    }
    return result;
}

std::set<std::string> NormalizeExtensionSet(const std::vector<std::string>& values) {
    std::set<std::string> result;
    for (const auto& value : values) {
        const auto normalized = NormalizeExtensionRule(value);
        if (!normalized.empty()) {
            result.insert(normalized);
        }
    }
    return result;
}

bool ContainsIgnoreCase(const std::vector<std::string>& values, const std::string& needle) {
    const auto normalizedNeedle = ToLower(needle);
    return std::any_of(values.begin(), values.end(), [&](const std::string& value) {
        return ToLower(value) == normalizedNeedle;
    });
}

std::vector<std::string> NormalizeDirFingerprintNameList(const std::vector<std::string>& values) {
    std::vector<std::string> result;
    for (const auto& value : values) {
        const auto normalized = NormalizeDirFingerprintRulePath(value);
        if (!normalized.empty() && !ContainsIgnoreCase(result, normalized)) {
            result.push_back(normalized);
        }
    }
    return result;
}

std::vector<std::string> NormalizeDirFingerprintExtensionList(const std::vector<std::string>& values) {
    std::vector<std::string> result;
    for (const auto& value : values) {
        const auto normalized = NormalizeExtensionRule(value);
        if (!normalized.empty() && !ContainsIgnoreCase(result, normalized)) {
            result.push_back(normalized);
        }
    }
    return result;
}

void AppendDirFingerprintNames(std::vector<std::string>& target, const std::vector<std::string>& values) {
    for (const auto& value : values) {
        const auto normalized = NormalizeDirFingerprintRulePath(value);
        if (!normalized.empty() && !ContainsIgnoreCase(target, normalized)) {
            target.push_back(normalized);
        }
    }
}

void AppendDirFingerprintExtensions(std::vector<std::string>& target, const std::vector<std::string>& values) {
    for (const auto& value : values) {
        const auto normalized = NormalizeExtensionRule(value);
        if (!normalized.empty() && !ContainsIgnoreCase(target, normalized)) {
            target.push_back(normalized);
        }
    }
}

std::string JsonUnescape(const std::string& value) {
    std::string result;
    result.reserve(value.size());
    bool escaping = false;
    for (char ch : value) {
        if (escaping) {
            switch (ch) {
            case 'n':
                result += '\n';
                break;
            case 'r':
                result += '\r';
                break;
            case 't':
                result += '\t';
                break;
            default:
                result += ch;
                break;
            }
            escaping = false;
            continue;
        }
        if (ch == '\\') {
            escaping = true;
            continue;
        }
        result += ch;
    }
    if (escaping) {
        result += '\\';
    }
    return result;
}

std::vector<std::string> ParseJsonStringArray(const std::string& json, const std::string& property, bool& found) {
    found = false;
    std::vector<std::string> result;
    const std::regex propertyRegex("\"" + property + R"("\s*:\s*\[([\s\S]*?)\])");
    std::smatch propertyMatch;
    if (!std::regex_search(json, propertyMatch, propertyRegex) || propertyMatch.size() < 2) {
        return result;
    }

    found = true;
    const std::string body = propertyMatch[1].str();
    const std::regex stringRegex(R"json("((?:\\.|[^"\\])*)")json");
    for (auto it = std::sregex_iterator(body.begin(), body.end(), stringRegex); it != std::sregex_iterator(); ++it) {
        result.push_back(JsonUnescape((*it)[1].str()));
    }
    return result;
}

std::vector<std::string> ReadRuleArray(
    const std::string& json,
    const std::vector<std::string>& propertyNames,
    const std::vector<std::string>& defaults) {
    for (const auto& propertyName : propertyNames) {
        bool found = false;
        auto values = ParseJsonStringArray(json, propertyName, found);
        if (found) {
            return values;
        }
    }
    return defaults;
}

std::vector<std::string> ReadOptionalRuleArray(
    const std::string& json,
    const std::vector<std::string>& propertyNames) {
    for (const auto& propertyName : propertyNames) {
        bool found = false;
        auto values = ParseJsonStringArray(json, propertyName, found);
        if (found) {
            return values;
        }
    }
    return {};
}

std::vector<std::string> ReadCombinedOptionalRuleArrays(
    const std::string& json,
    const std::vector<std::string>& propertyNames) {
    std::vector<std::string> result;
    for (const auto& propertyName : propertyNames) {
        bool found = false;
        auto values = ParseJsonStringArray(json, propertyName, found);
        if (found) {
            result.insert(result.end(), values.begin(), values.end());
        }
    }
    return result;
}

void AddNames(std::set<std::string>& target, const std::vector<std::string>& values) {
    const auto normalized = NormalizeNameSet(values);
    target.insert(normalized.begin(), normalized.end());
}

void AddExtensions(std::set<std::string>& target, const std::vector<std::string>& values) {
    const auto normalized = NormalizeExtensionSet(values);
    target.insert(normalized.begin(), normalized.end());
}

FileRules LoadFileRules(const fs::path& projectRoot, bool dirMode, bool includeArtifacts = false) {
    FileRules rules;
    rules.supportedExtensions = NormalizeExtensionSet(DefaultSupportedExtensions);
    rules.dirFingerprintIgnoredDirectories = NormalizeDirFingerprintNameList(DirIgnoredDirectories);
    rules.dirFingerprintIgnoredFileNames = {};
    rules.dirFingerprintIgnoredExtensions = NormalizeDirFingerprintExtensionList(DirIgnoredExtensions);

    if (dirMode) {
        rules.ignoredDirectories = NormalizeNameSet(DirIgnoredDirectories);
        rules.ignoredExtensions = NormalizeExtensionSet(DirIgnoredExtensions);
        rules.ignoredFileNames = {};
        if (!includeArtifacts) {
            AddNames(rules.ignoredDirectories, DirArtifactIgnoredDirectories);
            AddNames(rules.ignoredFileNames, DirArtifactIgnoredFileNames);
            AppendDirFingerprintNames(rules.dirFingerprintIgnoredDirectories, DirArtifactIgnoredDirectories);
            AppendDirFingerprintNames(rules.dirFingerprintIgnoredFileNames, DirArtifactIgnoredFileNames);
        }
    }
    else {
        rules.ignoredDirectories = NormalizeNameSet(DefaultIgnoredDirectories);
        rules.ignoredExtensions = NormalizeExtensionSet(DefaultIgnoredExtensions);
        rules.ignoredFileNames = NormalizeNameSet(DefaultIgnoredFileNames);
    }

    fs::path rulesPath;
    const auto envRulesPath = GetEnvText("CC_WORKBENCH_FILE_RULES_PATH");
    if (!envRulesPath.empty()) {
        rulesPath = fs::path(envRulesPath);
    }
    else {
        rulesPath = projectRoot / ".ccFileRules.json";
    }

    std::error_code ec;
    if (fs::exists(rulesPath, ec)) {
        try {
            const auto json = ReadFile(rulesPath);
            const auto ignoredDirectories = ReadOptionalRuleArray(json, { "IgnoredDirectories" });
            const auto ignoredExtensions = ReadOptionalRuleArray(json, { "IgnoredExtensions" });
            const auto ignoredFileNames = dirMode
                ? ReadCombinedOptionalRuleArrays(json, { "IgnoredFileNames", "IgnoredFiles" })
                : ReadOptionalRuleArray(json, { "IgnoredFiles", "IgnoredFileNames" });
            AddNames(rules.ignoredDirectories, ignoredDirectories);
            AddExtensions(rules.ignoredExtensions, ignoredExtensions);
            AddNames(rules.ignoredFileNames, ignoredFileNames);
            AddExtensions(rules.supportedExtensions, ReadOptionalRuleArray(json, { "SupportedExtensions" }));
            AppendDirFingerprintNames(rules.dirFingerprintIgnoredDirectories, ignoredDirectories);
            AppendDirFingerprintNames(rules.dirFingerprintIgnoredFileNames, ignoredFileNames);
            AppendDirFingerprintExtensions(rules.dirFingerprintIgnoredExtensions, ignoredExtensions);
        }
        catch (...) {
            // Invalid project rules fall back to defaults, mirroring the Workbench.
        }
    }

    if (!dirMode) {
        for (const auto& extension : rules.supportedExtensions) {
            rules.ignoredExtensions.erase(extension);
        }
    }
    return rules;
}

bool MatchesFileRule(const std::set<std::string>& rules, const std::string& relativePath, const std::string& fileName) {
    const auto normalizedPath = NormalizeRulePath(relativePath);
    const auto normalizedName = ToLower(fileName);
    for (const auto& rule : rules) {
        if (IsPathRule(rule)) {
            if (RuleMatchesText(normalizedPath, rule)) {
                return true;
            }
            continue;
        }
        if (RuleMatchesText(normalizedName, rule)) {
            return true;
        }
    }
    return false;
}

bool MatchesIgnoredDirectory(const FileRules& rules, const std::string& relativePath, const std::string& directoryName) {
    const auto normalizedPath = NormalizeRulePath(relativePath);
    const auto normalizedName = ToLower(directoryName);
    for (const auto& rule : rules.ignoredDirectories) {
        if (IsPathRule(rule)) {
            if (PathMatchesDirectoryRule(normalizedPath, rule)) {
                return true;
            }
            continue;
        }
        if (normalizedName == rule) {
            return true;
        }
    }

    std::stringstream parts(normalizedPath);
    std::string part;
    while (std::getline(parts, part, '/')) {
        if (rules.ignoredDirectories.count(part) > 0) {
            return true;
        }
    }
    return false;
}

bool IsBackupOrTemporaryFile(const std::string& fileName) {
    const auto lower = ToLower(fileName);
    return lower.find(".ccbak.") != std::string::npos ||
        EndsWith(lower, ".tmp") ||
        EndsWith(lower, "~") ||
        EndsWith(lower, ".snapshot");
}

bool IsDirManifestArtifactPath(const std::string& relSlash) {
    const auto clean = NormalizeRulePath(relSlash);
    if (clean.empty()) {
        return false;
    }

    std::stringstream parts(clean);
    std::string part;
    while (std::getline(parts, part, '/')) {
        if (!part.empty() && DirManifestArtifactParts.count(part) > 0) {
            return true;
        }
    }
    return false;
}

std::vector<FileInfo> WithoutDirManifestArtifacts(const std::vector<FileInfo>& files) {
    std::vector<FileInfo> result;
    result.reserve(files.size());
    for (const auto& file : files) {
        if (!IsDirManifestArtifactPath(file.relSlash)) {
            result.push_back(file);
        }
    }
    return result;
}

bool ShouldTrackFile(
    const FileRules& rules,
    const std::string& relativePath,
    const std::string& fileName,
    const std::string& extension,
    bool requireSupportedExtension) {
    const auto normalizedPath = NormalizeRulePath(relativePath);
    const auto normalizedExtension = NormalizeExtensionRule(extension);
    if (MatchesIgnoredDirectory(rules, normalizedPath, "")) {
        return false;
    }

    if (MatchesFileRule(rules.ignoredFileNames, normalizedPath, fileName)) {
        return false;
    }
    if (IsBackupOrTemporaryFile(fileName)) {
        return false;
    }
    if (rules.ignoredExtensions.count(normalizedExtension) > 0) {
        return false;
    }

    return !requireSupportedExtension || rules.supportedExtensions.count(normalizedExtension) > 0;
}

std::string KindOf(const std::string& relSlash) {
    const fs::path path(relSlash);
    const std::string name = path.filename().generic_string();
    const std::string lowerName = ToLower(name);
    const std::string ext = ToLower(path.extension().generic_string());

    if (lowerName == "cmakelists.txt") {
        return "cmake";
    }
    if (ext == ".cs") {
        return "csharp";
    }
    if (ext == ".csproj") {
        return "csharp-project";
    }
    if (ext == ".cpp" || ext == ".cc" || ext == ".cxx" || ext == ".c") {
        return ext == ".c" ? "c" : "cpp";
    }
    if (ext == ".h" || ext == ".hpp" || ext == ".hh" || ext == ".hxx") {
        return "cpp-header";
    }
    if (ext == ".axaml") {
        return "avalonia-xaml";
    }
    if (ext == ".xaml") {
        return "xaml";
    }
    if (ext == ".glsl" || ext == ".vert" || ext == ".frag" || ext == ".comp" || ext == ".geom" ||
        ext == ".tesc" || ext == ".tese" || ext == ".mesh" || ext == ".task") {
        return "shader";
    }
    if (ext == ".ps1") {
        return "powershell";
    }
    if (ext == ".md") {
        return "markdown";
    }
    if (ext == ".json") {
        return "json";
    }
    if (ext == ".ts") {
        return "typescript";
    }
    if (ext == ".tsx") {
        return "typescript-react";
    }
    if (ext == ".js") {
        return "javascript";
    }
    if (ext == ".py") {
        return "python";
    }
    if (ext == ".rs") {
        return "rust";
    }
    if (ext == ".go") {
        return "go";
    }

    return ext.empty() ? "file" : ext.substr(1);
}

bool IsFunctionKind(const std::string& kind) {
    static const std::set<std::string> kinds = {
        "csharp", "cpp", "c", "cpp-header", "powershell", "typescript", "typescript-react",
        "javascript", "python", "rust", "go"
    };
    return kinds.count(kind) > 0;
}

bool IsDirFunctionExportKind(const std::string& kind) {
    return IsFunctionKind(kind) && kind != "cpp-header";
}

bool IsTextKind(const std::string& kind) {
    static const std::set<std::string> kinds = {
        "csharp", "cpp", "c", "cpp-header", "powershell", "typescript", "typescript-react",
        "javascript", "python", "rust", "go", "avalonia-xaml", "xaml", "shader", "markdown",
        "json", "cmake", "file"
    };
    return kinds.count(kind) > 0 || !kind.empty();
}

std::string ExportsOf(const std::string& kind) {
    return (IsFunctionKind(kind) && kind != "cpp-header") ? "full,function,find" : "full,find";
}

std::string TokenRole(std::string value, const std::string& fallback) {
    value = Trim(std::move(value));
    if (value.empty()) {
        return fallback;
    }

    static const std::vector<std::string> acronyms = {
        "DCCM", "QUIC", "HTTP", "ADNL", "RLDP", "GPU", "CPU", "FFI", "LLM", "API",
        "DHT", "SVO", "VM", "UI", "AO"
    };
    for (const auto& acronym : acronyms) {
        value = std::regex_replace(value, std::regex(acronym), " " + acronym + " ");
    }
    value = std::regex_replace(value, std::regex("([A-Z]+)([A-Z][a-z])"), "$1 $2");
    value = std::regex_replace(value, std::regex("([a-z0-9])([A-Z])"), "$1 $2");
    value = std::regex_replace(value, std::regex("[_\\.-]+"), " ");
    value = ToLower(value);

    std::stringstream words(value);
    std::string word;
    std::vector<std::string> selected;
    while (words >> word) {
        selected.push_back(word);
        if (selected.size() == 5) {
            break;
        }
    }
    if (selected.empty()) {
        return fallback;
    }

    std::ostringstream out;
    for (std::size_t index = 0; index < selected.size(); ++index) {
        if (index > 0) {
            out << " ";
        }
        out << selected[index];
    }
    return out.str();
}

std::string RoleOf(const std::string& relSlash, const std::string& kind) {
    const std::string lower = ToLower(relSlash);
    const std::string name = ToLower(fs::path(relSlash).filename().generic_string());

    if (name == "ccdir.ps1") {
        return "dir manifest entrypoint";
    }
    if (name == "cc.ps1") {
        return "source export entrypoint";
    }
    if (name == "cmakelists.txt") {
        return "cmake build configuration";
    }
    if (name == "package.json") {
        return "node package manifest";
    }
    if (name == "main.cpp" || name == "main.c" || name == "program.cs") {
        return "runtime entrypoint";
    }
    if (lower.find("mainwindow") != std::string::npos) {
        return "main UI";
    }
    if (kind == "csharp" && lower.find("workbench") != std::string::npos) {
        return TokenRole(fs::path(relSlash).stem().generic_string(), "workbench") + " source";
    }
    if (lower.find("viewmodel") != std::string::npos) {
        return "viewmodel state";
    }
    if (lower.find("prompt") != std::string::npos) {
        return "prompt workflow";
    }
    if (lower.find("renderer") != std::string::npos) {
        return "renderer";
    }
    if (kind == "shader") {
        if (lower.find("fullscreen") != std::string::npos) {
            return "fullscreen shader";
        }
        return "shaders shader";
    }
    if (kind == "markdown") {
        return "project overview documentation";
    }
    if (kind == "specialcc") {
        return "feature";
    }
    if (lower.find("legacyhidden") != std::string::npos) {
        return "legacy hidden source";
    }
    if (lower.find("hiddenbyrules") != std::string::npos) {
        return "hidden by rules source";
    }
    if (lower.find("hidden") != std::string::npos) {
        return "hidden source";
    }
    if (lower.find("largefixture") != std::string::npos) {
        return "large fixture source";
    }
    if (kind == "cpp-header") {
        return "header declaration";
    }
    return kind + " source";
}

bool IsBuildFile(const std::string& relSlash) {
    const std::string lower = ToLower(Slashes(relSlash));
    const std::string name = ToLower(fs::path(relSlash).filename().generic_string());
    static const std::set<std::string> exact = {
        "cmakelists.txt", "cargo.toml", "cargo.lock", "package.json", "tsconfig.json",
        "pyproject.toml", "setup.py", "requirements.txt", "go.mod", "go.sum",
        "pom.xml", "build.gradle", "settings.gradle", "gradle.properties",
        "project.godot", "global.json", "appsettings.json"
    };
    if (exact.count(name) > 0) {
        return true;
    }
    return EndsWith(name, ".csproj") || EndsWith(name, ".fsproj") || EndsWith(name, ".vbproj") ||
        EndsWith(name, ".sln") || EndsWith(name, ".props") || EndsWith(name, ".targets") ||
        StartsWith(name, "vite.config.") || StartsWith(name, "next.config.") ||
        StartsWith(name, "webpack.config.") || lower == "src/main.rs" || lower == "build.rs";
}

int AnchorScoreOf(const std::string& relSlash, const std::string& kind) {
    const std::string lower = ToLower(relSlash);
    const std::string name = ToLower(fs::path(relSlash).filename().generic_string());

    if (name == "cc.ps1" || name == "ccdir.ps1" || name == "ccreplace.ps1") {
        return 100;
    }
    if (IsBuildFile(relSlash)) {
        return 100;
    }
    if (name == "main.cpp" || name == "main.c" || name == "program.cs" || name == "app.py") {
        return 90;
    }
    if (lower.find("mainwindow") != std::string::npos) {
        return 90;
    }
    if (lower.find("renderer") != std::string::npos && kind != "cpp-header") {
        return 80;
    }
    if (kind == "cpp-header") {
        return 70;
    }
    if (kind == "shader") {
        return 60;
    }
    if (kind == "csharp" || kind == "cpp" || kind == "c" || kind == "powershell") {
        return 10;
    }
    return 0;
}

std::string FenceLanguage(const std::string& kind) {
    if (kind == "cpp" || kind == "c" || kind == "cpp-header") {
        return "cpp";
    }
    if (kind == "powershell") {
        return "powershell";
    }
    if (kind == "python") {
        return "python";
    }
    if (kind == "json") {
        return "json";
    }
    if (kind == "cmake") {
        return "cmake";
    }
    if (kind == "markdown") {
        return "markdown";
    }
    if (kind == "shader") {
        return "glsl";
    }
    return "";
}

std::string JoinLines(const std::vector<std::string>& lines, int start, int end) {
    if (start < 0 || end < start || end > static_cast<int>(lines.size())) {
        return "";
    }

    std::ostringstream out;
    for (int index = start; index < end; ++index) {
        if (index > start) {
            out << "\n";
        }
        out << lines[static_cast<std::size_t>(index)];
    }
    return out.str();
}

std::string RegionHashFromLines(const std::vector<std::string>& lines, int start, int end) {
    if (start < 0 || end < start || end > static_cast<int>(lines.size())) {
        return "";
    }
    return Sha1Short(JoinLines(lines, start, end));
}

std::string StripCodeLineForHashScan(const std::string& line, bool& inBlockComment) {
    std::string result;
    result.reserve(line.size());

    bool inString = false;
    bool inChar = false;
    bool escape = false;
    std::size_t index = 0;
    while (index < line.size()) {
        const char ch = line[index];
        const char next = index + 1 < line.size() ? line[index + 1] : '\0';

        if (inBlockComment) {
            if (ch == '*' && next == '/') {
                inBlockComment = false;
                result += "  ";
                index += 2;
                continue;
            }
            result += ' ';
            ++index;
            continue;
        }

        if (!inString && !inChar) {
            if (ch == '/' && next == '/') {
                break;
            }
            if (ch == '/' && next == '*') {
                inBlockComment = true;
                result += "  ";
                index += 2;
                continue;
            }
        }

        if (escape) {
            result += ' ';
            escape = false;
            ++index;
            continue;
        }

        if ((inString || inChar) && ch == '\\') {
            result += ' ';
            escape = true;
            ++index;
            continue;
        }

        if (!inChar && ch == '"') {
            inString = !inString;
            result += ' ';
            ++index;
            continue;
        }

        if (!inString && ch == '\'') {
            inChar = !inChar;
            result += ' ';
            ++index;
            continue;
        }

        result += (inString || inChar) ? ' ' : ch;
        ++index;
    }

    return result;
}

bool LooksLikeHashableFunctionPrefix(const std::string& prefix) {
    const auto clean = Trim(prefix);
    if (clean.empty()) {
        return true;
    }
    if (clean.find('=') != std::string::npos || clean.find(',') != std::string::npos ||
        clean.find('[') != std::string::npos || clean.find(']') != std::string::npos ||
        clean.find('.') != std::string::npos) {
        return false;
    }

    static const std::regex rejected(R"(\b(return|if|while|for|switch|case|sizeof|new|delete|catch)\b)", std::regex::icase);
    return !std::regex_search(clean, rejected);
}

std::string FunctionLeafName(const std::string& symbol) {
    auto clean = Trim(symbol);
    const auto pos = clean.find_last_of(':');
    if (pos != std::string::npos) {
        while (!clean.empty() && clean.back() == ':') {
            clean.pop_back();
        }
        const auto scopedPos = clean.find_last_of(':');
        if (scopedPos != std::string::npos) {
            clean = clean.substr(scopedPos + 1);
        }
    }
    return Trim(clean);
}

bool IsRejectedGenericFunctionLeaf(const std::string& leaf) {
    static const std::set<std::string> rejected = {
        "if", "for", "while", "switch", "return", "sizeof", "catch",
        "static_cast", "reinterpret_cast", "const_cast", "dynamic_cast"
    };
    return rejected.count(ToLower(leaf)) > 0;
}

std::vector<FunctionRange> FindHashableFunctionRanges(
    const std::string& path,
    const std::vector<std::string>& lines,
    int maxCount) {
    std::vector<FunctionRange> results;
    std::set<std::string> seen;
    const std::string ext = ToLower(fs::path(path).extension().generic_string());

    bool blockComment = false;
    const std::regex psFunction(R"(^\s*function\s+([A-Za-z0-9_\-]+)\b)", std::regex::icase);
    const std::regex gdFunction(R"(^\s*(static\s+)?func\s+([A-Za-z_][A-Za-z0-9_]*)\s*\()", std::regex::icase);
    const std::regex csExpression(
        R"(^\s*((public|private|protected|internal|static|virtual|override|sealed|new|required|readonly|partial|unsafe|async)\s+)+[A-Za-z0-9_<>,\.\?\[\]\s]+[ \t]+([A-Za-z_][A-Za-z0-9_]*)\s*=>)",
        std::regex::icase);
    const std::regex csProperty(
        R"(^\s*((public|private|protected|internal|static|virtual|override|sealed|new|required|readonly|partial|unsafe|async)\s+)+[A-Za-z0-9_<>,\.\?\[\]\s]+[ \t]+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(get|set|init)\b)",
        std::regex::icase);
    const std::regex genericFunction(R"((([A-Za-z_][A-Za-z0-9_]*::)*~?[A-Za-z_][A-Za-z0-9_]*)\s*\()");

    for (std::size_t i = 0; i < lines.size(); ++i) {
        const std::string rawLine = lines[i];
        if (Trim(rawLine).empty()) {
            continue;
        }

        const std::string cleanLine = StripCodeLineForHashScan(rawLine, blockComment);
        std::string name;
        bool isFunction = false;
        bool isExpressionMember = false;
        std::smatch match;

        if (ext == ".ps1") {
            if (std::regex_search(cleanLine, match, psFunction) && match.size() > 1) {
                name = match[1].str();
                isFunction = true;
            }
        }
        else if (ext == ".gd") {
            if (std::regex_search(cleanLine, match, gdFunction) && match.size() > 2) {
                name = match[2].str();
                isFunction = true;
            }
        }
        else if (ext == ".cs" && std::regex_search(cleanLine, match, csExpression) && match.size() > 3) {
            name = match[3].str();
            isFunction = true;
            isExpressionMember = true;
        }
        else if (ext == ".cs" && std::regex_search(cleanLine, match, csProperty) && match.size() > 3) {
            name = match[3].str();
            isFunction = true;
        }
        else if (std::regex_search(cleanLine, match, genericFunction) && match.size() > 1) {
            const auto candidate = match[1].str();
            const auto leafPos = candidate.rfind("::");
            const auto leaf = leafPos == std::string::npos ? candidate : candidate.substr(leafPos + 2);
            const auto startPos = static_cast<std::size_t>(match.position(1));
            const bool safeBoundary = startPos == 0 ||
                !(std::isalnum(static_cast<unsigned char>(cleanLine[startPos - 1])) ||
                    cleanLine[startPos - 1] == '_' ||
                    cleanLine[startPos - 1] == '~');
            if (safeBoundary && !IsRejectedGenericFunctionLeaf(leaf) &&
                LooksLikeHashableFunctionPrefix(cleanLine.substr(0, startPos))) {
                name = leaf;
                isFunction = true;
            }
        }

        if (!isFunction || name.empty()) {
            continue;
        }

        if (isExpressionMember) {
            int end = -1;
            bool expressionBlockComment = false;
            for (std::size_t j = i; j < lines.size(); ++j) {
                const auto clean = StripCodeLineForHashScan(lines[j], expressionBlockComment);
                if (clean.find(';') != std::string::npos) {
                    end = static_cast<int>(j + 1);
                    break;
                }
            }
            if (end < 0) {
                continue;
            }

            const int start = static_cast<int>(i);
            const std::string key = ToLower(name + "|" + std::to_string(start) + "|" + std::to_string(end));
            if (!seen.insert(key).second) {
                continue;
            }
            results.push_back({ name, start, end, RegionHashFromLines(lines, start, end) });
            if (static_cast<int>(results.size()) >= maxCount) {
                break;
            }
            continue;
        }

        int openLine = -1;
        bool sawSemicolonBeforeBrace = false;
        bool scanBlockComment = false;
        for (std::size_t j = i; j < lines.size(); ++j) {
            const auto clean = StripCodeLineForHashScan(lines[j], scanBlockComment);
            const auto braceIndex = clean.find('{');
            const auto semiIndex = clean.find(';');
            if (semiIndex != std::string::npos && (braceIndex == std::string::npos || semiIndex < braceIndex)) {
                sawSemicolonBeforeBrace = true;
                break;
            }
            if (braceIndex != std::string::npos) {
                openLine = static_cast<int>(j);
                break;
            }
        }
        if (openLine < 0 || sawSemicolonBeforeBrace) {
            continue;
        }

        int start = static_cast<int>(i);
        while (start > 0) {
            const auto previous = Trim(lines[static_cast<std::size_t>(start - 1)]);
            if (previous.empty()) {
                break;
            }
            if (StartsWith(previous, "template") || StartsWith(previous, "[[") ||
                StartsWith(previous, "__attribute__") || StartsWith(previous, "VKAPI_ATTR")) {
                --start;
                continue;
            }
            break;
        }

        int depth = 0;
        bool started = false;
        bool bodyBlockComment = false;
        int end = -1;
        for (std::size_t j = static_cast<std::size_t>(openLine); j < lines.size(); ++j) {
            const auto clean = StripCodeLineForHashScan(lines[j], bodyBlockComment);
            for (char ch : clean) {
                if (ch == '{') {
                    ++depth;
                    started = true;
                }
                else if (ch == '}') {
                    --depth;
                }
                if (started && depth == 0) {
                    end = static_cast<int>(j + 1);
                    break;
                }
            }
            if (end >= 0) {
                break;
            }
        }
        if (end < 0) {
            continue;
        }

        const std::string key = ToLower(name + "|" + std::to_string(start) + "|" + std::to_string(end));
        if (!seen.insert(key).second) {
            continue;
        }
        results.push_back({ name, start, end, RegionHashFromLines(lines, start, end) });
        if (static_cast<int>(results.size()) >= maxCount) {
            break;
        }
    }

    return results;
}

std::vector<FunctionRange> FindReplaceRegionHashes(const std::vector<std::string>& lines) {
    std::vector<FunctionRange> regions;
    std::string activeName;
    int activeStart = -1;
    const std::regex beginRegex(R"(CC-REPLACE-BEGIN\s*:\s*([^\s]+))");
    const std::regex endRegex(R"(CC-REPLACE-END\s*:\s*([^\s]+))");

    for (std::size_t i = 0; i < lines.size(); ++i) {
        std::smatch match;
        if (activeStart < 0 && std::regex_search(lines[i], match, beginRegex) && match.size() > 1) {
            activeName = Trim(match[1].str());
            activeStart = static_cast<int>(i + 1);
            continue;
        }

        if (activeStart >= 0 && std::regex_search(lines[i], match, endRegex) && match.size() > 1) {
            const auto endName = Trim(match[1].str());
            if (endName == activeName) {
                const int end = static_cast<int>(i);
                regions.push_back({ activeName, activeStart, end, RegionHashFromLines(lines, activeStart, end) });
            }
            activeName.clear();
            activeStart = -1;
        }
    }
    return regions;
}

void AddHashHintsForFile(std::ostringstream& out, const std::string& path, const std::vector<std::string>& lines) {
    out << "Hash hints for optional HASH: patch headers:\n";
    out << "- whole_file HASH: " << Sha1Short(JoinLines(lines, 0, static_cast<int>(lines.size()))) << "\n";

    for (const auto& region : FindReplaceRegionHashes(lines)) {
        out << "- replace_region " << region.name << " HASH: " << region.hash << "\n";
    }

    const auto functions = FindHashableFunctionRanges(path, lines, 40);
    for (const auto& range : functions) {
        out << "- function " << range.name << " HASH: " << range.hash << "\n";
    }
    if (functions.size() >= 40) {
        out << "- function hash list truncated at 40 entries to avoid token bloat\n";
    }
    out << "\n";
}

std::vector<FileInfo> ScanFiles(
    const fs::path& root,
    const FileRules& rules,
    bool requireSupportedExtension,
    int maxDepth = 1000000,
    const std::string& resolvedProfile = "generic",
    bool includeAllTopLevel = true) {
    std::vector<FileInfo> files;
    std::error_code ec;

    if (!fs::exists(root, ec)) {
        throw std::runtime_error("Project root does not exist: " + root.string());
    }

    fs::recursive_directory_iterator it(root, fs::directory_options::skip_permission_denied, ec);
    fs::recursive_directory_iterator end;
    for (; it != end; it.increment(ec)) {
        if (ec) {
            ec.clear();
            continue;
        }

        const fs::path current = it->path();
        const std::string rel = RelativeSlash(root, current);
        const int depth = it.depth();
        if (depth >= maxDepth) {
            if (it->is_directory(ec)) {
                it.disable_recursion_pending();
            }
            continue;
        }

        if (depth == 0 && !ShouldIncludeTopLevelItem(rel, includeAllTopLevel, resolvedProfile)) {
            if (it->is_directory(ec)) {
                it.disable_recursion_pending();
            }
            continue;
        }

        if (it->is_directory(ec)) {
            if (depth == 0 &&
                ToLower(current.filename().generic_string()) == "contextcontrol" &&
                ToLower(rel) == "contextcontrol") {
                it.disable_recursion_pending();
                continue;
            }

            if (MatchesIgnoredDirectory(rules, rel, current.filename().generic_string())) {
                it.disable_recursion_pending();
            }
            continue;
        }

        if (!it->is_regular_file(ec)) {
            continue;
        }

        if (!ShouldTrackFile(rules, rel, current.filename().generic_string(), current.extension().generic_string(), requireSupportedExtension)) {
            continue;
        }

        FileInfo info;
        info.fullPath = current;
        info.relSlash = rel;
        info.relNative = NativeDisplay(rel);
        info.name = current.filename().generic_string();
        info.ext = ExtensionOf(current);
        info.kind = KindOf(rel);
        info.size = fs::file_size(current, ec);
        if (ec) {
            info.size = 0;
            ec.clear();
        }

        if (IsTextKind(info.kind)) {
            files.push_back(info);
        }
    }

    std::sort(files.begin(), files.end(), [](const FileInfo& left, const FileInfo& right) {
        return ToLower(left.relSlash) < ToLower(right.relSlash);
    });
    return files;
}

const FileInfo* FindFile(const std::vector<FileInfo>& files, const std::string& relSlash) {
    const std::string wanted = ToLower(Slashes(relSlash));
    for (const auto& file : files) {
        if (ToLower(file.relSlash) == wanted) {
            return &file;
        }
    }
    return nullptr;
}

bool HasRequestWildcard(const std::string& value) {
    return value.find('*') != std::string::npos || value.find('?') != std::string::npos;
}

std::string CleanRequestPath(std::string value) {
    value = Slashes(Trim(std::move(value)));
    while (StartsWith(value, "./")) {
        value = value.substr(2);
    }
    while (!value.empty() && value.front() == '/') {
        value.erase(value.begin());
    }
    return value;
}

bool IsUnderRoot(const fs::path& root, const fs::path& path) {
    std::error_code ec;
    auto rootPath = fs::weakly_canonical(root, ec);
    if (ec) {
        ec.clear();
        rootPath = fs::absolute(root, ec);
    }

    auto candidate = fs::weakly_canonical(path, ec);
    if (ec) {
        ec.clear();
        candidate = fs::absolute(path, ec);
    }

    const auto rootText = ToLower(Slashes(rootPath.generic_string()));
    const auto candidateText = ToLower(Slashes(candidate.generic_string()));
    return candidateText == rootText || StartsWith(candidateText, rootText + "/");
}

bool ShouldIncludeExplicitRequestFile(
    const FileRules& rules,
    const std::string& relativePath,
    const std::string& fileName,
    const std::string& extension) {
    const auto normalizedPath = NormalizeRulePath(relativePath);
    const auto normalizedExtension = NormalizeExtensionRule(extension);
    if (MatchesFileRule(rules.ignoredFileNames, normalizedPath, fileName)) {
        return false;
    }
    if (IsBackupOrTemporaryFile(fileName)) {
        return false;
    }
    if (rules.ignoredExtensions.count(normalizedExtension) > 0) {
        return false;
    }

    return rules.supportedExtensions.count(normalizedExtension) > 0;
}

void AddExplicitRequestFileIfNeeded(std::vector<FileInfo>& files, const fs::path& root, const FileRules& rules, const std::string& requestPath) {
    const auto rel = CleanRequestPath(requestPath);
    if (rel.empty() || HasRequestWildcard(rel) || FindFile(files, rel) != nullptr) {
        return;
    }

    const auto full = root / fs::path(rel);
    std::error_code ec;
    if (!fs::exists(full, ec) || !fs::is_regular_file(full, ec) || !IsUnderRoot(root, full)) {
        return;
    }
    if (!ShouldIncludeExplicitRequestFile(rules, rel, full.filename().generic_string(), full.extension().generic_string())) {
        return;
    }

    FileInfo info;
    info.fullPath = full;
    info.relSlash = rel;
    info.relNative = NativeDisplay(rel);
    info.name = full.filename().generic_string();
    info.ext = ExtensionOf(full);
    info.kind = KindOf(rel);
    info.size = fs::file_size(full, ec);
    if (ec) {
        info.size = 0;
        ec.clear();
    }

    if (IsTextKind(info.kind)) {
        files.push_back(info);
    }
}

void AddExplicitRequestFiles(std::vector<FileInfo>& files, const fs::path& root, const FileRules& rules, const std::vector<std::string>& paths, const std::vector<std::pair<std::string, std::string>>& scopedFunctions) {
    for (const auto& path : paths) {
        AddExplicitRequestFileIfNeeded(files, root, rules, path);
    }
    for (const auto& request : scopedFunctions) {
        AddExplicitRequestFileIfNeeded(files, root, rules, request.first);
    }

    std::sort(files.begin(), files.end(), [](const FileInfo& left, const FileInfo& right) {
        return ToLower(left.relSlash) < ToLower(right.relSlash);
    });
}

std::vector<const FileInfo*> CandidateTextFiles(const std::vector<FileInfo>& files) {
    std::vector<const FileInfo*> result;
    for (const auto& file : files) {
        if (IsTextKind(file.kind)) {
            result.push_back(&file);
        }
    }
    return result;
}

std::string ParentSlash(const std::string& relSlash) {
    const auto pos = relSlash.find_last_of('/');
    if (pos == std::string::npos) {
        return "";
    }
    return relSlash.substr(0, pos + 1);
}

std::string TopRoot(const std::string& relSlash) {
    const auto pos = relSlash.find('/');
    if (pos == std::string::npos) {
        return "";
    }
    return relSlash.substr(0, pos + 1);
}

std::string StableRoot(const std::string& relSlash) {
    const auto parent = ParentSlash(relSlash);
    if (parent.empty()) {
        return "";
    }

    const std::string top = TopRoot(relSlash);
    if (top == "src/" || top == "source/" || top == "sources/") {
        std::vector<std::string> parts;
        std::stringstream ss(parent);
        std::string item;
        while (std::getline(ss, item, '/')) {
            if (!item.empty()) {
                parts.push_back(item);
            }
        }
        if (parts.size() >= 2) {
            return parts[0] + "/" + parts[1] + "/";
        }
    }

    return parent;
}

std::vector<std::string> SelectRoots(const std::vector<FileInfo>& files, const std::string& scope) {
    std::map<std::string, int> counts;
    const bool includeNestedRoots = !scope.empty() || files.size() <= 60;
    for (const auto& file : files) {
        if (!scope.empty() && !StartsWith(ToLower(file.relSlash), ToLower(scope))) {
            continue;
        }

        const std::string top = TopRoot(file.relSlash);
        const std::string stable = StableRoot(file.relSlash);
        const std::string parent = ParentSlash(file.relSlash);
        if (!top.empty()) {
            counts[top]++;
        }
        if (!stable.empty()) {
            counts[stable]++;
        }
        if (includeNestedRoots) {
            std::string current = parent;
            while (!current.empty()) {
                counts[current]++;
                const auto trimmed = current.substr(0, current.size() - 1);
                const auto pos = trimmed.find_last_of('/');
                if (pos == std::string::npos) {
                    break;
                }
                current = trimmed.substr(0, pos + 1);
            }
        }
    }

    std::vector<std::pair<std::string, int>> ranked(counts.begin(), counts.end());
    std::sort(ranked.begin(), ranked.end(), [](const auto& left, const auto& right) {
        if (left.second != right.second) {
            return left.second > right.second;
        }
        return left.first < right.first;
    });

    std::vector<std::string> result;
    for (const auto& item : ranked) {
        result.push_back(item.first);
        if (result.size() >= 16) {
            break;
        }
    }

    auto rootPriority = [&](const std::string& root) {
        const auto lower = ToLower(root);
        if (StartsWith(lower, "src/") || StartsWith(lower, "source/") || StartsWith(lower, "sources/")) {
            return 0;
        }

        bool hasSource = false;
        bool hasShader = false;
        bool hasDocs = lower.find("doc") != std::string::npos;
        for (const auto& file : files) {
            if (!StartsWith(ToLower(file.relSlash), lower)) {
                continue;
            }
            if (IsFunctionKind(file.kind) || file.kind == "cpp-header" || file.kind == "csharp") {
                hasSource = true;
            }
            if (file.kind == "shader") {
                hasShader = true;
            }
            if (file.kind == "markdown") {
                hasDocs = true;
            }
        }
        if (hasSource) {
            return 1;
        }
        if (hasShader) {
            return 2;
        }
        if (hasDocs) {
            return 3;
        }
        return 4;
    };

    std::sort(result.begin(), result.end(), [&](const std::string& left, const std::string& right) {
        const int leftPriority = rootPriority(left);
        const int rightPriority = rootPriority(right);
        if (leftPriority != rightPriority) {
            return leftPriority < rightPriority;
        }
        const int leftCount = counts.count(left) > 0 ? counts[left] : 0;
        const int rightCount = counts.count(right) > 0 ? counts[right] : 0;
        if (leftCount != rightCount) {
            return leftCount > rightCount;
        }
        const auto leftDepth = std::count(left.begin(), left.end(), '/');
        const auto rightDepth = std::count(right.begin(), right.end(), '/');
        if (leftDepth != rightDepth) {
            return leftDepth < rightDepth;
        }
        return ToLower(left) < ToLower(right);
    });
    return result;
}

int RootFileCount(const std::vector<FileInfo>& files, const std::string& root) {
    return static_cast<int>(std::count_if(files.begin(), files.end(), [&](const FileInfo& file) {
        return StartsWith(ToLower(file.relSlash), ToLower(root));
    }));
}

std::string RootRole(const std::string& root) {
    const std::string lower = ToLower(root);
    if (lower.find("custom-visible") != std::string::npos) {
        return "custom visible";
    }
    if (lower.find("custom-hidden") != std::string::npos) {
        return "custom hidden";
    }
    if (lower == "src/app/ui/") {
        return "app source";
    }
    if (lower.find("app") != std::string::npos) {
        return "app source";
    }
    if (lower.find("shader") != std::string::npos) {
        return "shader programs";
    }
    if (lower.find("doc") != std::string::npos) {
        return "docs";
    }
    if (lower.find("ui") != std::string::npos || lower.find("view") != std::string::npos) {
        return "UI files";
    }
    if (lower.find("test") != std::string::npos) {
        return "tests";
    }
    return "source files";
}

int RoutingPriorityOf(const FileInfo* file) {
    const std::string lower = ToLower(file->relSlash);
    const std::string name = ToLower(file->name);
    if (name == "cc.ps1" || name == "ccdir.ps1" || name == "ccreplace.ps1" ||
        StartsWith(lower, "lib/cc.dir.") || StartsWith(lower, "lib/cc.export.") ||
        StartsWith(lower, "lib/cc.replace.")) {
        return 1000;
    }
    if (name == "main.cpp" || name == "main.c" || name == "program.cs" || name == "app.py") {
        return 900;
    }
    if (name == "mainwindow.axaml" || name == "mainwindow.xaml" ||
        name == "mainwindow.axaml.cs" || name == "mainwindow.xaml.cs") {
        return 900;
    }
    if (lower.find("renderer") != std::string::npos || lower.find("promptviewmodel") != std::string::npos) {
        return 800;
    }
    if (file->kind == "avalonia-xaml" || file->kind == "xaml" || lower.find("/ui/") != std::string::npos ||
        lower.find("mainwindow") != std::string::npos) {
        return 700;
    }
    if (file->kind == "shader") {
        return 650;
    }
    if (file->kind == "powershell") {
        return 450;
    }
    if (IsBuildFile(file->relSlash)) {
        return 250;
    }
    if (file->kind == "markdown") {
        return 40;
    }
    if (IsFunctionKind(file->kind)) {
        return 800;
    }
    return 50;
}

std::vector<const FileInfo*> SelectAnchors(const std::vector<FileInfo>& files, bool scoped, const std::string& scope) {
    std::vector<const FileInfo*> anchors;
    const bool tinyProject = files.size() <= 60;
    for (const auto& file : files) {
        if (scoped && !StartsWith(ToLower(file.relSlash), ToLower(scope))) {
            continue;
        }
        if (scoped || tinyProject) {
            anchors.push_back(&file);
            continue;
        }

        const std::string lower = ToLower(file.relSlash);
        const std::string name = ToLower(file.name);
        const bool highSignal = name == "cc.ps1" || name == "ccdir.ps1" || name == "cmakelists.txt" ||
            name == "package.json" || name == "main.cpp" || lower.find("mainwindow") != std::string::npos ||
            lower.find("renderer") != std::string::npos || lower.find("promptviewmodel") != std::string::npos ||
            file.kind == "shader";
        if (highSignal) {
            anchors.push_back(&file);
        }
    }

    std::sort(anchors.begin(), anchors.end(), [](const FileInfo* left, const FileInfo* right) {
        const int leftPriority = RoutingPriorityOf(left);
        const int rightPriority = RoutingPriorityOf(right);
        if (leftPriority != rightPriority) {
            return leftPriority > rightPriority;
        }
        const int leftScore = AnchorScoreOf(left->relSlash, left->kind);
        const int rightScore = AnchorScoreOf(right->relSlash, right->kind);
        if (leftScore != rightScore) {
            return leftScore > rightScore;
        }
        return ToLower(left->relSlash) < ToLower(right->relSlash);
    });

    if (!scoped && anchors.size() > 32) {
        anchors.resize(32);
    }
    return anchors;
}

std::vector<std::string> ExtractSymbols(const FileInfo& file) {
    std::vector<std::string> symbols;
    const bool isXaml = file.kind == "avalonia-xaml" || file.kind == "xaml";
    if (!IsFunctionKind(file.kind) && !isXaml) {
        return symbols;
    }

    std::string text;
    try {
        text = ReadFile(file.fullPath);
    }
    catch (...) {
        return symbols;
    }

    std::set<std::string> seen;
    auto addSymbol = [&](std::string value) {
        value = Trim(value);
        if (value.empty() || symbols.size() >= 8) {
            return;
        }

        if (seen.insert(value).second) {
            symbols.push_back(value);
        }
    };

    if (text.size() > 160000) {
        text.resize(160000);
    }

    if (isXaml) {
        const std::regex classRegex("(?:x:Class|Class)\\s*=\\s*\"([^\"]+)\"");
        const std::regex nameRegex("(?:x:Name|Name)\\s*=\\s*\"([A-Za-z_][A-Za-z0-9_.-]*)\"");
        const std::regex bindingRegex("\\{Binding\\s+([A-Za-z_][A-Za-z0-9_.]*)");
        for (std::sregex_iterator it(text.begin(), text.end(), classRegex), end; it != end && symbols.size() < 8; ++it) {
            addSymbol((*it)[1].str());
        }
        for (std::sregex_iterator it(text.begin(), text.end(), nameRegex), end; it != end && symbols.size() < 8; ++it) {
            addSymbol((*it)[1].str());
        }
        for (std::sregex_iterator it(text.begin(), text.end(), bindingRegex), end; it != end && symbols.size() < 8; ++it) {
            addSymbol((*it)[1].str());
        }
    }
    else if (file.kind == "powershell") {
        const std::regex functionRegex(R"(^\s*function\s+([A-Za-z_][\w.-]*))");
        const auto lines = SplitLines(text);
        for (const auto& line : lines) {
            std::smatch match;
            if (std::regex_search(line, match, functionRegex) && match.size() > 1) {
                addSymbol(match[1].str());
            }
        }
    }
    else if (file.kind == "csharp") {
        const std::regex typeRegex(R"(^\s*(?:public|private|protected|internal|sealed|static|partial|abstract|\s)*(?:class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*))");
        const std::regex memberRegex(R"(^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|partial)\s+)+[\w<>\[\],.?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\()");
        const auto lines = SplitLines(text);
        for (const auto& line : lines) {
            std::smatch match;
            if (std::regex_search(line, match, typeRegex) && match.size() > 1) {
                addSymbol(match[1].str());
            }
            if (std::regex_search(line, match, memberRegex) && match.size() > 1) {
                addSymbol(match[1].str());
            }
        }
    }
    else {
        const std::regex functionRegex(R"(^\s*(?:[\w:<>,~*&\s]+)\s+([A-Za-z_~][\w:~]*)\s*\([^;]*\)\s*(?:const\s*)?\{)");
        const auto lines = SplitLines(text);
        for (const auto& line : lines) {
            std::smatch match;
            if (std::regex_search(line, match, functionRegex) && match.size() > 1) {
                addSymbol(match[1].str());
            }
        }
    }

    return symbols;
}

std::string NowUtcIso() {
    const auto now = std::chrono::system_clock::now();
    const std::time_t time = std::chrono::system_clock::to_time_t(now);
    std::tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &time);
#else
    gmtime_r(&time, &utc);
#endif
    char buffer[64]{};
    std::strftime(buffer, sizeof(buffer), "%Y-%m-%dT%H:%M:%SZ", &utc);
    return buffer;
}

std::string FileListFingerprint(const std::vector<FileInfo>& files) {
    std::vector<std::string> paths;
    paths.reserve(files.size());
    for (const auto& file : files) {
        paths.push_back(ToLower(file.relSlash));
    }
    std::sort(paths.begin(), paths.end());

    std::ostringstream text;
    for (std::size_t index = 0; index < paths.size(); ++index) {
        if (index > 0) {
            text << "\n";
        }
        text << paths[index];
    }
    return Sha256Text(text.str());
}

std::vector<std::uint16_t> Utf8ToUtf16CodeUnits(const std::string& value) {
    std::vector<std::uint16_t> result;
    for (std::size_t index = 0; index < value.size();) {
        const unsigned char lead = static_cast<unsigned char>(value[index]);
        std::uint32_t codePoint = 0;
        std::size_t width = 1;
        if (lead < 0x80) {
            codePoint = lead;
        }
        else if ((lead & 0xE0) == 0xC0 && index + 1 < value.size()) {
            codePoint = lead & 0x1F;
            width = 2;
        }
        else if ((lead & 0xF0) == 0xE0 && index + 2 < value.size()) {
            codePoint = lead & 0x0F;
            width = 3;
        }
        else if ((lead & 0xF8) == 0xF0 && index + 3 < value.size()) {
            codePoint = lead & 0x07;
            width = 4;
        }
        else {
            result.push_back(static_cast<std::uint16_t>(lead));
            ++index;
            continue;
        }

        bool valid = true;
        for (std::size_t offset = 1; offset < width; ++offset) {
            const unsigned char trail = static_cast<unsigned char>(value[index + offset]);
            if ((trail & 0xC0) != 0x80) {
                valid = false;
                break;
            }
            codePoint = (codePoint << 6) | (trail & 0x3F);
        }

        if (!valid) {
            result.push_back(static_cast<std::uint16_t>(lead));
            ++index;
            continue;
        }

        if (codePoint <= 0xFFFF) {
            result.push_back(static_cast<std::uint16_t>(codePoint));
        }
        else {
            codePoint -= 0x10000;
            result.push_back(static_cast<std::uint16_t>(0xD800 + (codePoint >> 10)));
            result.push_back(static_cast<std::uint16_t>(0xDC00 + (codePoint & 0x3FF)));
        }
        index += width;
    }
    return result;
}

std::int32_t WindowsPowerShellStringHash(const std::string& value) {
    const auto chars = Utf8ToUtf16CodeUnits(value);
    std::int32_t hash1 = 5381;
    std::int32_t hash2 = 5381;

    for (std::size_t index = 0; index < chars.size();) {
        hash1 = static_cast<std::int32_t>((static_cast<std::uint32_t>(hash1) * 33U) ^ chars[index]);
        ++index;
        if (index >= chars.size()) {
            break;
        }
        hash2 = static_cast<std::int32_t>((static_cast<std::uint32_t>(hash2) * 33U) ^ chars[index]);
        ++index;
    }

    return static_cast<std::int32_t>(
        static_cast<std::uint32_t>(hash1) +
        (static_cast<std::uint32_t>(hash2) * 1566083941U));
}

std::int32_t FileRuleFingerprint(const FileRules& rules) {
    std::ostringstream text;
    auto appendList = [&](const std::vector<std::string>& values) {
        for (const auto& value : values) {
            if (text.tellp() > 0) {
                text << "|";
            }
            text << value;
        }
    };
    appendList(rules.dirFingerprintIgnoredDirectories);
    appendList(rules.dirFingerprintIgnoredFileNames);
    appendList(rules.dirFingerprintIgnoredExtensions);
    return WindowsPowerShellStringHash(text.str());
}

std::map<std::string, std::string> MajorManifestHashes(const std::vector<FileInfo>& files) {
    std::map<std::string, std::string> hashes;
    for (const auto& file : files) {
        if (IsBuildFile(file.relSlash)) {
            hashes[file.relSlash] = Sha256File(file.fullPath);
        }
    }
    return hashes;
}

std::vector<std::string> StackLabels(const std::vector<FileInfo>& files) {
    std::set<std::string> paths;
    std::set<std::string> kinds;
    for (const auto& file : files) {
        paths.insert(ToLower(file.relSlash));
        kinds.insert(file.kind);
    }

    std::vector<std::string> stacks;
    auto hasKind = [&](const std::string& kind) { return kinds.count(kind) > 0; };
    auto hasPath = [&](const std::string& path) { return paths.count(path) > 0; };
    auto add = [&](const std::string& value) {
        if (std::find(stacks.begin(), stacks.end(), value) == stacks.end()) {
            stacks.push_back(value);
        }
    };

    if (hasPath("cargo.toml") || hasKind("rust")) {
        add("Rust");
    }
    if (hasPath("package.json") || hasKind("typescript") || hasKind("typescript-react") || hasKind("javascript")) {
        add("JavaScript/TypeScript");
    }
    if (hasPath("pom.xml") || hasPath("build.gradle") || hasKind("java") || hasKind("kotlin")) {
        add("Java/Kotlin");
    }
    if (hasPath("go.mod") || hasKind("go")) {
        add("Go");
    }
    if (hasPath("pyproject.toml") || hasPath("setup.py") || hasKind("python")) {
        add("Python");
    }
    if (hasPath("cmakelists.txt") || hasKind("cpp") || hasKind("c") || hasKind("cpp-header")) {
        add("C/C++");
    }
    if (hasKind("csharp") || hasKind("csharp-project") || hasKind("avalonia-xaml")) {
        add(".NET/Avalonia");
    }
    if (hasKind("shader")) {
        add("Shaders");
    }
    if (hasKind("powershell")) {
        add("PowerShell");
    }
    return stacks;
}

std::vector<std::string> SplitFamilyName(const std::string& stem) {
    std::vector<std::string> result;
    std::string current;
    for (const char ch : stem) {
        if (ch == '.' || ch == '_' || ch == '-') {
            if (!current.empty()) {
                result.push_back(current);
                current.clear();
            }
            continue;
        }
        current += ch;
    }
    if (!current.empty()) {
        result.push_back(current);
    }
    return result;
}

std::string FamilyPrefixOf(const std::string& relSlash, bool profileMode) {
    const auto stem = fs::path(relSlash).stem().generic_string();
    if (stem.empty()) {
        return "";
    }

    const auto segments = SplitFamilyName(stem);
    if (segments.size() > 1 && segments[0].size() >= 3) {
        if (ToLower(segments[0]) == "cc" && segments.size() > 1) {
            return segments[0] + "." + segments[1];
        }
        return segments[0];
    }

    if (!profileMode) {
        return "";
    }

    std::smatch match;
    if (std::regex_search(stem, match, std::regex(R"(^([A-Z][A-Za-z0-9]{3,}?)(?=[A-Z][a-z]|$))")) &&
        match.size() > 1) {
        return match[1].str();
    }
    return "";
}

std::vector<FamilyRecord> BuildFamilyRecords(const std::vector<FileInfo>& files, bool profileMode) {
    static const std::set<std::string> genericPrefixes = {
        "app", "base", "core", "local", "project", "external", "browser", "chat", "main",
        "config", "service", "manager", "controller", "view"
    };

    std::map<std::string, std::vector<std::string>> groups;
    std::map<std::string, std::tuple<std::string, std::string, std::string>> partsByKey;
    for (const auto& file : files) {
        if (!IsDirFunctionExportKind(file.kind)) {
            continue;
        }

        const auto prefix = FamilyPrefixOf(file.relSlash, profileMode);
        if (prefix.empty() || prefix.size() < 3) {
            continue;
        }
        if (profileMode && genericPrefixes.count(ToLower(prefix)) > 0) {
            continue;
        }

        const auto dir = NormalizeRulePath(fs::path(file.relSlash).parent_path().generic_string());
        const auto ext = fs::path(file.relSlash).extension().generic_string();
        if (profileMode && dir.empty()) {
            continue;
        }

        const auto key = dir + "|" + prefix + "|" + ext;
        groups[key].push_back(file.relSlash);
        partsByKey[key] = std::make_tuple(dir, prefix, ext);
    }

    std::vector<FamilyRecord> families;
    for (const auto& entry : groups) {
        if (entry.second.size() < 2) {
            continue;
        }

        const auto& parts = partsByKey[entry.first];
        const auto& dir = std::get<0>(parts);
        const auto& prefix = std::get<1>(parts);
        const auto& ext = std::get<2>(parts);
        FamilyRecord family;
        family.path = dir.empty() ? prefix + "*" + ext : dir + "/" + prefix + "*" + ext;
        family.role = "split " + TokenRole(prefix, prefix) + " family";
        family.exports = "wildcard-function,find";
        family.score = 40;
        if (StartsWith(ToLower(prefix), "cc.")) {
            family.score = 100;
        }
        else if (std::regex_search(prefix, std::regex(R"(Workbench|Skillbook|MainWindow|CodeEditor|ProjectGraph|ProjectTree|LocalLlmCatalog|RenderControl)", std::regex::icase))) {
            family.score = 80;
        }
        families.push_back(family);
    }

    std::sort(families.begin(), families.end(), [](const FamilyRecord& left, const FamilyRecord& right) {
        if (left.score != right.score) {
            return left.score > right.score;
        }
        return left.path < right.path;
    });
    if (profileMode && families.size() > 8) {
        families.resize(8);
    }
    return families;
}

std::string BuildProfileJson(const fs::path& root, const std::vector<FileInfo>& files, const FileRules& rules) {
    std::map<std::string, int> languageCounts;
    for (const auto& file : files) {
        languageCounts[file.kind]++;
    }

    const auto selectableFiles = WithoutDirManifestArtifacts(files);
    const auto roots = SelectRoots(selectableFiles, "");
    const auto anchors = SelectAnchors(selectableFiles, false, "");
    const auto families = BuildFamilyRecords(selectableFiles, true);
    const auto manifestHashes = MajorManifestHashes(files);
    const auto stacks = StackLabels(files);

    std::ostringstream out;
    out << "{\n";
    out << "  \"SchemaVersion\": 1,\n";
    out << "  \"GeneratedUtc\": \"" << EscapeJson(NowUtcIso()) << "\",\n";
    out << "  \"ProjectRoot\": \"" << EscapeJson(root.string()) << "\",\n";
    out << "  \"ProfileSource\": \"ccDir.ps1\",\n";
    out << "  \"FileRuleFingerprint\": " << FileRuleFingerprint(rules) << ",\n";
    out << "  \"FileListFingerprint\": \"" << FileListFingerprint(files) << "\",\n";
    out << "  \"VisibleFileCount\": " << files.size() << ",\n";
    out << "  \"RootCounts\": {\n";
    for (std::size_t index = 0; index < roots.size(); ++index) {
        out << "    \"" << EscapeJson(roots[index]) << "\": " << RootFileCount(files, roots[index]);
        out << (index + 1 == roots.size() ? "\n" : ",\n");
    }
    out << "  },\n";
    out << "  \"MajorManifestHashes\": {\n";
    for (auto it = manifestHashes.begin(); it != manifestHashes.end(); ++it) {
        out << "    \"" << EscapeJson(it->first) << "\": \"" << EscapeJson(it->second) << "\"";
        out << (std::next(it) == manifestHashes.end() ? "\n" : ",\n");
    }
    out << "  },\n";
    out << "  \"Languages\": {\n";
    for (auto it = languageCounts.begin(); it != languageCounts.end(); ++it) {
        out << "    \"" << EscapeJson(it->first) << "\": " << it->second;
        out << (std::next(it) == languageCounts.end() ? "\n" : ",\n");
    }
    out << "  },\n";
    out << "  \"Stacks\": [\n";
    for (std::size_t index = 0; index < stacks.size(); ++index) {
        out << "    \"" << EscapeJson(stacks[index]) << "\"";
        out << (index + 1 == stacks.size() ? "\n" : ",\n");
    }
    out << "  ],\n";
    out << "  \"Roots\": [\n";
    for (std::size_t index = 0; index < roots.size(); ++index) {
        out << "    { \"Path\": \"" << EscapeJson(roots[index]) << "\", \"Role\": \""
            << EscapeJson(RootRole(roots[index])) << "\", \"Files\": " << RootFileCount(files, roots[index]) << " }";
        out << (index + 1 == roots.size() ? "\n" : ",\n");
    }
    out << "  ],\n";
    out << "  \"Files\": [\n";
    for (std::size_t index = 0; index < anchors.size(); ++index) {
        const auto* file = anchors[index];
        out << "    { \"Path\": \"" << EscapeJson(file->relSlash) << "\", \"Kind\": \""
            << EscapeJson(file->kind) << "\", \"Role\": \"" << EscapeJson(RoleOf(file->relSlash, file->kind))
            << "\", \"Exports\": \"" << EscapeJson(ExportsOf(file->kind)) << "\", \"Score\": "
            << AnchorScoreOf(file->relSlash, file->kind) << " }";
        out << (index + 1 == anchors.size() ? "\n" : ",\n");
    }
    out << "  ],\n";
    out << "  \"Families\": [\n";
    for (std::size_t index = 0; index < families.size(); ++index) {
        const auto& family = families[index];
        out << "    { \"Path\": \"" << EscapeJson(family.path) << "\", \"Role\": \""
            << EscapeJson(family.role) << "\", \"Exports\": \"" << EscapeJson(family.exports) << "\" }";
        out << (index + 1 == families.size() ? "\n" : ",\n");
    }
    out << "  ]\n";
    out << "}\n";
    return out.str();
}

std::string NormalizeScope(std::string scope) {
    scope = Slashes(Trim(scope));
    while (StartsWith(scope, "./")) {
        scope = scope.substr(2);
    }
    while (!scope.empty() && scope.front() == '/') {
        scope.erase(scope.begin());
    }
    if (!scope.empty() && scope.back() != '/') {
        scope += "/";
    }
    return scope;
}

std::string BuildDirManifest(const std::vector<FileInfo>& files, const Options& options) {
    const bool scoped = options.lod == 1;
    const std::string scope = scoped ? NormalizeScope(options.scope) : "";
    const auto selectableFiles = WithoutDirManifestArtifacts(files);
    const auto roots = SelectRoots(selectableFiles, scope);
    const auto anchors = SelectAnchors(selectableFiles, scoped, scope);
    std::vector<FileInfo> familyFiles;
    if (scoped) {
        for (const auto& file : selectableFiles) {
            if (StartsWith(ToLower(file.relSlash), ToLower(scope))) {
                familyFiles.push_back(file);
            }
        }
    }
    else {
        familyFiles = selectableFiles;
    }
    const auto families = BuildFamilyRecords(familyFiles, !scoped);

    std::ostringstream out;
    out << "CC-DIR-MANIFEST-V2\n";
    out << "LOD: " << (scoped ? "L1_SCOPED" : "L0_GLOBAL") << "\n";
    if (scoped) {
        out << "SCOPE: " << scope << "\n";
    }
    out << "\n";
    out << "REQUEST_GRAMMAR:\n";
    out << "Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.\n";
    out << "FIND lines may repeat before END; do not mix FIND with source or EXPAND lines.\n";
    out << "EXPAND must be the only request line before END.\n";
    out << "FUNC may appear only with final source request lines.\n";
    out << "\n";
    out << "ROOTS:\n";
    if (roots.empty()) {
        out << "(none)\n";
    }
    else {
        for (const auto& root : roots) {
            out << "ROOT path=\"" << root << "\" role=\"" << RootRole(root) << "\" files=" << RootFileCount(files, root) << "\n";
        }
    }
    out << "\n";
    out << "FILES:\n";
    if (anchors.empty()) {
        out << "(none)\n";
    }
    else {
        for (const auto* file : anchors) {
            out << "FILE path=\"" << file->relSlash << "\" tier=" << (scoped ? "L1" : "L0")
                << " kind=\"" << file->kind << "\" role=\"" << RoleOf(file->relSlash, file->kind)
                << "\" exports=\"" << ExportsOf(file->kind) << "\"";
            const auto symbols = ExtractSymbols(*file);
            if (!symbols.empty()) {
                std::ostringstream symbolField;
                std::size_t fieldLength = 0;
                bool wroteAny = false;
                for (std::size_t index = 0; index < symbols.size(); ++index) {
                    const std::size_t separatorLength = wroteAny ? 1 : 0;
                    if (fieldLength + separatorLength + symbols[index].size() > 160) {
                        break;
                    }
                    if (wroteAny) {
                        symbolField << ",";
                        fieldLength += 1;
                    }
                    symbolField << symbols[index];
                    fieldLength += symbols[index].size();
                    wroteAny = true;
                }
                if (wroteAny) {
                    out << " symbols=\"" << symbolField.str() << "\"";
                }
            }
            out << "\n";
        }
    }
    out << "\n";
    out << "FAMILIES:\n";
    if (families.empty()) {
        out << "(none)\n";
    }
    else {
        for (const auto& family : families) {
            out << "FAMILY path=\"" << family.path << "\" role=\"" << family.role
                << "\" exports=\"" << family.exports << "\"\n";
        }
    }
    out << "\n";
    out << "VALID_OUTPUT_EXAMPLES:\n";
    if (scoped && !anchors.empty()) {
        out << anchors.front()->relSlash << "\n";
        if (IsFunctionKind(anchors.front()->kind)) {
            out << "FUNCTION " << anchors.front()->relSlash << " :: <symbol>\n";
        }
    }
    else if (!anchors.empty()) {
        out << anchors.front()->relSlash << "\n";
        if (!roots.empty()) {
            out << "EXPAND: " << roots.front() << "\n";
        }
        out << "FIND: <text>\n";
    }
    out << "END\n";
    out << "\n";
    out << "FINAL_CHECK:\n";
    out << "Return only valid request lines ending with END.\n";
    return out.str();
}

Options ParseOptions(const std::vector<std::string>& args, const std::string& defaultOutput) {
    Options options;
    options.outputFile = defaultOutput;
    for (std::size_t index = 0; index < args.size(); ++index) {
        const std::string arg = args[index];
        const std::string lower = ToLower(arg);
        auto takeValue = [&](std::string& target) {
            if (index + 1 >= args.size()) {
                throw std::runtime_error("Missing value after " + arg);
            }
            target = args[++index];
        };
        auto takeInt = [&](int& target) {
            if (index + 1 >= args.size()) {
                throw std::runtime_error("Missing value after " + arg);
            }
            target = std::stoi(args[++index]);
        };

        if (lower == "-outputfile") {
            takeValue(options.outputFile);
        }
        else if (lower == "-maxdepth") {
            takeInt(options.maxDepth);
        }
        else if (lower == "-profile") {
            takeValue(options.profile);
        }
        else if (lower == "-lod") {
            takeInt(options.lod);
        }
        else if (lower == "-scope") {
            takeValue(options.scope);
        }
        else if (lower == "-profileonly") {
            options.profileOnly = true;
        }
        else if (lower == "-profileoutput") {
            takeValue(options.profileOutput);
        }
        else if (lower == "-profilefile") {
            takeValue(options.profileFile);
        }
        else if (lower == "-maxfilekb") {
            takeInt(options.maxFileKB);
        }
        else if (lower == "-forcelargefiles") {
            options.forceLargeFiles = true;
        }
        else if (lower == "-includeartifacts") {
            options.includeArtifacts = true;
        }
        else if (lower == "-includealltoplevel") {
            options.includeAllTopLevel = true;
        }
        else if (lower == "-noclipboard") {
            options.noClipboard = true;
        }
        else if (lower == "-hashhints") {
            options.hashHints = true;
        }
    }
    return options;
}

std::string ExtractDescription(const FileInfo& file, const std::string& text) {
    const auto lines = SplitLines(text);
    const std::regex ccDesc(R"(CC-DESC\s*:\s*(.+)$)", std::regex::icase);
    for (std::size_t index = 0; index < lines.size() && index < 120; ++index) {
        std::smatch match;
        if (std::regex_search(lines[index], match, ccDesc) && match.size() > 1) {
            std::string desc = Trim(match[1].str());
            if (EndsWith(desc, "*/")) {
                desc = Trim(desc.substr(0, desc.size() - 2));
            }
            if (EndsWith(desc, "-->")) {
                desc = Trim(desc.substr(0, desc.size() - 3));
            }
            return desc;
        }
    }

    if (file.kind == "cpp-header") {
        return "No CC-DESC found. Header declaration.";
    }
    return "No CC-DESC found.";
}

void AddCodeExportHeader(std::ostringstream& out, const fs::path& root) {
    out << "# Code export\n\n";
    out << "Generated from project files.\n\n";
    out << "Project root: " << root.string() << "\n\n";
    out << "## Instructions for Context Control\n\n";
    out << "This export is source context only. Use the standing Context Control instructions for workflow rules and patch format.\n\n";
    out << "Minimal rules for this turn:\n";
    out << "- Spend reasoning on the requested code fix, not tool mechanics.\n";
    out << "- Prefer a single patch.txt containing raw BEGIN CC-REPLACE blocks. Inline only if tiny.\n";
    out << "- Keep the existing architecture and modular ownership boundaries.\n";
    out << "- Use MODE: insert_include for include-only edits.\n";
    out << "- FIND: reports are discovery only; they never include source bodies. Request exact files/functions after discovery.\n";
    out << "- If this export contains Hash hints, copy the matching HASH: value into function/replace_region patch headers. If no hash hint is present, omit HASH:.\n";
    out << "- If critical context is missing, ask only for exact paths, FUNCTION exports, or FIND discovery queries, one per line, ending with END.\n\n";
    out << "Default CMake build, when applicable: cmake --build build --config Release -j\n\n";
}

void AddFileBlock(std::ostringstream& out, const FileInfo& file, const Options& options) {
    out << "\n## " << file.relNative << "\n\n";

    std::string content;
    try {
        content = ReadFile(file.fullPath);
    }
    catch (...) {
        out << "MISSING: " << file.relSlash << "\n";
        return;
    }

    out << "Description: " << ExtractDescription(file, content) << "\n\n";
    const auto sizeKb = static_cast<int>((file.size + 1023) / 1024);
    if (!options.forceLargeFiles && sizeKb > options.maxFileKB) {
        out << "Skipped large text file: " << file.relNative << " (" << sizeKb << " KB > " << options.maxFileKB << " KB).\n";
        out << "Re-run cc.ps1 with -ForceLargeFiles if this exact file is truly needed.\n";
        return;
    }

    const auto lines = SplitLines(content);
    if (options.hashHints) {
        AddHashHintsForFile(out, file.relSlash, lines);
    }

    out << "````" << FenceLanguage(file.kind) << "\n";
    out << content;
    if (!content.empty() && content.back() != '\n') {
        out << "\n";
    }
    out << "````\n";
}

void AddDirectoryTree(std::ostringstream& out, const fs::path& root, const fs::path& dir, const FileRules& rules) {
    out << "\n## Folder tree: " << NativeDisplay(RelativeSlash(root, dir)) << "\n\n";
    out << "```text\n";
    std::vector<std::string> entries;
    std::error_code ec;
    for (fs::recursive_directory_iterator it(dir, fs::directory_options::skip_permission_denied, ec), end; it != end; it.increment(ec)) {
        if (ec) {
            ec.clear();
            continue;
        }
        const std::string rel = RelativeSlash(dir, it->path());
        const std::string projectRel = RelativeSlash(root, it->path());
        if (it->is_directory(ec)) {
            if (MatchesIgnoredDirectory(rules, projectRel, it->path().filename().generic_string())) {
                it.disable_recursion_pending();
            }
            entries.push_back(rel + "/");
            continue;
        }
        if (!ShouldTrackFile(rules, projectRel, it->path().filename().generic_string(), it->path().extension().generic_string(), true)) {
            continue;
        }
        entries.push_back(rel);
    }
    std::sort(entries.begin(), entries.end());
    for (const auto& entry : entries) {
        out << entry << "\n";
    }
    out << "```\n";
}

bool WildcardMatchCI(const std::string& pattern, const std::string& value) {
    std::string regexText;
    regexText.reserve(pattern.size() * 2);
    for (char ch : pattern) {
        switch (ch) {
        case '*':
            regexText += ".*";
            break;
        case '?':
            regexText += ".";
            break;
        case '.':
        case '\\':
        case '/':
        case '+':
        case '^':
        case '$':
        case '(':
        case ')':
        case '[':
        case ']':
        case '{':
        case '}':
        case '|':
            regexText += '\\';
            regexText += ch == '\\' ? '/' : ch;
            break;
        default:
            regexText += ch;
            break;
        }
    }

    return std::regex_match(ToLower(Slashes(value)), std::regex(ToLower(regexText)));
}

std::vector<std::string> ExtractFunctionBlockLines(const FileInfo& file, const std::string& symbol, int& startLine) {
    const auto lines = SplitLines(ReadFile(file.fullPath));
    const std::string symbolLower = ToLower(symbol);
    for (std::size_t index = 0; index < lines.size(); ++index) {
        if (!ContainsCI(lines[index], symbolLower)) {
            continue;
        }

        std::vector<std::string> block;
        startLine = static_cast<int>(index + 1);

        if (lines[index].find("=>") != std::string::npos) {
            for (std::size_t j = index; j < lines.size(); ++j) {
                block.push_back(lines[j]);
                if (lines[j].find(';') != std::string::npos) {
                    break;
                }
            }
            return block;
        }

        std::size_t openLine = index;
        while (openLine < lines.size() && lines[openLine].find('{') == std::string::npos) {
            ++openLine;
        }
        if (openLine >= lines.size()) {
            block.push_back(lines[index]);
            return block;
        }

        int depth = 0;
        for (std::size_t j = index; j < lines.size(); ++j) {
            block.push_back(lines[j]);
            for (char ch : lines[j]) {
                if (ch == '{') {
                    ++depth;
                }
                else if (ch == '}') {
                    --depth;
                }
            }
            if (j >= openLine && depth <= 0) {
                break;
            }
        }
        return block;
    }

    startLine = 0;
    return {};
}

std::vector<FunctionRange> FindMatchingFunctionRanges(const FileInfo& file, const std::string& symbol) {
    const auto lines = SplitLines(ReadFile(file.fullPath));
    const auto ranges = FindHashableFunctionRanges(file.relSlash, lines, 100000);
    const auto leaf = FunctionLeafName(symbol);
    std::vector<FunctionRange> matches;
    for (const auto& range : ranges) {
        if (ToLower(range.name) == ToLower(leaf)) {
            matches.push_back(range);
        }
    }
    return matches;
}

void AddFunctionRangeBlock(
    std::ostringstream& out,
    const FileInfo& file,
    const std::string& symbol,
    const FunctionRange& range,
    const std::vector<std::string>& lines,
    const Options& options) {
    out << "Source: " << file.relSlash << " lines " << (range.start + 1) << "-" << range.end << "\n";
    if (options.hashHints) {
        out << "Hash hint for optional CC-REPLACE header: MODE: function | NAME: " << symbol << " | HASH: " << range.hash << "\n";
    }
    out << "\n````" << FenceLanguage(file.kind) << "\n";
    out << JoinLines(lines, range.start, range.end) << "\n";
    out << "````\n\n";
}

void AddScopedFunction(std::ostringstream& out, const std::vector<FileInfo>& files, const std::string& pathPattern, const std::string& symbol, const Options& options) {
    out << "\n## FUNCTION " << pathPattern << " :: " << symbol << "\n\n";
    int matches = 0;
    for (const auto& file : files) {
        const bool pathMatches = pathPattern.find('*') != std::string::npos
            ? WildcardMatchCI(pathPattern, file.relSlash)
            : ToLower(Slashes(pathPattern)) == ToLower(file.relSlash);
        if (!pathMatches) {
            continue;
        }

        const auto lines = SplitLines(ReadFile(file.fullPath));
        const auto ranges = FindMatchingFunctionRanges(file, symbol);
        if (!ranges.empty()) {
            for (const auto& range : ranges) {
                ++matches;
                AddFunctionRangeBlock(out, file, symbol, range, lines, options);
            }
            continue;
        }

        int startLine = 0;
        auto block = ExtractFunctionBlockLines(file, symbol, startLine);
        if (!block.empty()) {
            ++matches;
            out << "Source: " << file.relSlash << " lines " << startLine << "-" << (startLine + static_cast<int>(block.size()) - 1) << "\n";
            out << "\n````" << FenceLanguage(file.kind) << "\n";
            for (const auto& line : block) {
                out << line << "\n";
            }
            out << "````\n\n";
        }
    }

    if (matches == 0) {
        out << "No matching function body found.\n\n";
    }
}

void AddGlobalFunction(std::ostringstream& out, const std::vector<FileInfo>& files, const std::string& symbol, const Options& options) {
    out << "\n## FOUND FUNCTION: " << symbol << "\n\n";
    int matches = 0;
    for (const auto& file : files) {
        if (!IsFunctionKind(file.kind)) {
            continue;
        }

        const auto lines = SplitLines(ReadFile(file.fullPath));
        const auto ranges = FindMatchingFunctionRanges(file, symbol);
        if (!ranges.empty()) {
            for (const auto& range : ranges) {
                ++matches;
                AddFunctionRangeBlock(out, file, symbol, range, lines, options);
            }
            continue;
        }

        int startLine = 0;
        auto block = ExtractFunctionBlockLines(file, symbol, startLine);
        if (!block.empty()) {
            ++matches;
            out << "Source: " << file.relSlash << " lines " << startLine << "-" << (startLine + static_cast<int>(block.size()) - 1) << "\n";
            out << "\n````" << FenceLanguage(file.kind) << "\n";
            for (const auto& line : block) {
                out << line << "\n";
            }
            out << "````\n\n";
        }
    }

    if (matches == 0) {
        out << "No matching code file found for function symbol: " << symbol << "\n\n";
    }
}

void AddFind(std::ostringstream& out, const std::vector<FileInfo>& files, const std::string& pattern) {
    out << "\n## FIND: " << pattern << "\n\n";
    std::vector<const FileInfo*> matches;
    for (const auto& file : files) {
        if (!IsTextKind(file.kind)) {
            continue;
        }
        std::string text;
        try {
            text = ReadFile(file.fullPath);
        }
        catch (...) {
            continue;
        }
        if (ContainsCI(text, pattern)) {
            matches.push_back(&file);
        }
    }

    if (matches.empty()) {
        out << "No matching code file found for: " << pattern << "\n\n";
        return;
    }

    out << "Matched code files only; file contents were not exported.\n";
    out << "Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.\n\n";
    out << "Matched code files:\n";
    for (const auto* file : matches) {
        out << "- " << file->relSlash << "\n";
    }

    out << "\nOccurrence preview:\n";
    for (const auto* file : matches) {
        const auto lines = SplitLines(ReadFile(file->fullPath));
        int emitted = 0;
        for (std::size_t index = 0; index < lines.size() && emitted < 4; ++index) {
            if (ContainsCI(lines[index], pattern)) {
                out << "- " << file->relSlash << ":" << (index + 1) << ": " << Trim(lines[index]) << "\n";
                ++emitted;
            }
        }
    }
    out << "\n";
}

std::vector<std::string> ReadRequestLines() {
    std::vector<std::string> lines;
    std::string line;
    while (std::getline(std::cin, line)) {
        line = Trim(line);
        if (line.empty() || ToLower(line) == "end") {
            break;
        }
        lines.push_back(line);
    }
    return lines;
}

std::vector<std::string> ExpandAutoDependencies(const fs::path& root, const std::vector<FileInfo>& files, const std::vector<std::string>& inputPaths) {
    std::vector<std::string> expanded;
    std::set<std::string> seen;
    auto add = [&](const std::string& rel) {
        const std::string key = ToLower(Slashes(rel));
        if (seen.insert(key).second) {
            expanded.push_back(rel);
        }
    };

    for (const auto& input : inputPaths) {
        add(input);
        const auto* file = FindFile(files, input);
        if (file == nullptr) {
            continue;
        }

        if (file->kind == "cpp" || file->kind == "c") {
            const std::string base = fs::path(file->relSlash).stem().generic_string();
            const std::string parent = ParentSlash(file->relSlash);
            for (const auto& ext : { ".h", ".hpp", ".hh", ".hxx" }) {
                const std::string header = parent + base + ext;
                if (FindFile(files, header) != nullptr) {
                    add(header);
                }
            }
        }
        else if (file->kind == "shader") {
            const auto lines = SplitLines(ReadFile(file->fullPath));
            const std::regex includeRegex(R"(^\s*#\s*include\s*[<"]([^>"]+)[>"])");
            for (const auto& line : lines) {
                std::smatch match;
                if (!std::regex_search(line, match, includeRegex) || match.size() < 2) {
                    continue;
                }
                const auto includePath = fs::weakly_canonical(file->fullPath.parent_path() / match[1].str());
                std::error_code ec;
                if (fs::exists(includePath, ec)) {
                    add(RelativeSlash(root, includePath));
                }
            }
        }
    }
    return expanded;
}

int RunDir(const std::vector<std::string>& args) {
    const auto root = ResolveProjectRoot();
    const auto options = ParseOptions(args, "cc_project_dir.md");
    const auto rules = LoadFileRules(root, true, options.includeArtifacts);
    const auto resolvedProfile = DetectDirProfile(root, options.profile);
    const auto files = ScanFiles(root, rules, false, options.maxDepth, resolvedProfile, options.includeAllTopLevel);

    if (options.profileOnly) {
        const std::string profilePath = options.profileOutput.empty() ? ".ccDirProfile.json" : options.profileOutput;
        WriteFile(profilePath, BuildProfileJson(root, files, rules));
        std::cout << "DIR profile created: " << profilePath << "\n";
        return 0;
    }

    if (options.lod == 0) {
        const std::string profilePath = options.profileFile.empty() ? ".ccDirProfile.json" : options.profileFile;
        WriteFile(profilePath, BuildProfileJson(root, files, rules));
    }

    WriteFile(options.outputFile, BuildDirManifest(files, options));
    std::cout << "Created: " << options.outputFile << "\n";
    return 0;
}

int RunCc(const std::vector<std::string>& args) {
    const auto root = ResolveProjectRoot();
    const auto options = ParseOptions(args, "cc_code_export.md");
    const auto rules = LoadFileRules(root, false);
    auto files = ScanFiles(root, rules, true);
    const auto requestLines = ReadRequestLines();

    std::vector<std::pair<std::string, std::string>> scopedFunctions;
    std::vector<std::string> globalFunctions;
    std::vector<std::string> finds;
    std::vector<std::string> paths;

    const std::regex scopedRegex(R"(^(FUNC|FUNCTION|FIND|SYMBOL)\s+(.+?)\s+::\s*(.+?)\s*$)", std::regex::icase);
    const std::regex colonRegex(R"(^(FUNC|FUNCTION|FIND|SYMBOL)\s*:\s*(.+?)\s*$)", std::regex::icase);
    const std::regex lenientRegex(R"(^(FUNC|FUNCTION)\s+(.+?)\s*$)", std::regex::icase);

    for (const auto& clean : requestLines) {
        std::smatch match;
        if (std::regex_match(clean, match, scopedRegex)) {
            const std::string kind = ToLower(match[1].str());
            if (kind == "symbol") {
                throw std::runtime_error("SYMBOL: is disabled because it exported whole matching files and caused token explosions. Use FIND for discovery, or request an exact path/FUNCTION export.");
            }
            if (kind == "find") {
                throw std::runtime_error("Malformed FIND request: " + clean);
            }
            scopedFunctions.emplace_back(match[2].str(), match[3].str());
            continue;
        }

        if (std::regex_match(clean, match, colonRegex)) {
            const std::string kind = ToLower(match[1].str());
            const std::string value = match[2].str();
            if (kind == "find") {
                finds.push_back(value);
            }
            else if (kind == "symbol") {
                throw std::runtime_error("SYMBOL: is disabled because it exported whole matching files and caused token explosions. Use FIND: " + value + " for discovery, or request an exact path/FUNCTION export.");
            }
            else {
                globalFunctions.push_back(value);
            }
            continue;
        }

        if (std::regex_match(clean, match, lenientRegex) && match.size() > 2) {
            const std::string value = match[2].str();
            if (value.find('/') == std::string::npos && value.find('\\') == std::string::npos) {
                globalFunctions.push_back(value);
                continue;
            }
        }

        if (StartsWith(ToLower(clean), "func") || StartsWith(ToLower(clean), "function") ||
            StartsWith(ToLower(clean), "find") || StartsWith(ToLower(clean), "symbol")) {
            throw std::runtime_error("Malformed request: " + clean);
        }

        paths.push_back(clean);
    }

    AddExplicitRequestFiles(files, root, rules, paths, scopedFunctions);

    std::ostringstream out;
    AddCodeExportHeader(out, root);

    for (const auto& pathText : ExpandAutoDependencies(root, files, paths)) {
        const std::string rel = Slashes(pathText);
        const auto full = root / fs::path(rel);
        std::error_code ec;
        if (!fs::exists(full, ec)) {
            out << "\n## MISSING: " << pathText << "\n\n";
            continue;
        }
        if (fs::is_directory(full, ec)) {
            AddDirectoryTree(out, root, full, rules);
            continue;
        }
        const auto* file = FindFile(files, rel);
        if (file == nullptr) {
            out << "\n## SKIPPED EXCLUDED/GENERATED PATH: " << pathText << "\n\n";
            continue;
        }
        AddFileBlock(out, *file, options);
    }

    for (const auto& request : scopedFunctions) {
        AddScopedFunction(out, files, request.first, request.second, options);
    }
    for (const auto& symbol : globalFunctions) {
        AddGlobalFunction(out, files, symbol, options);
    }
    for (const auto& find : finds) {
        AddFind(out, files, find);
    }

    WriteFile(options.outputFile, out.str());
    std::cout << "Created: " << options.outputFile << "\n";
    return 0;
}

void PrintHelp() {
    std::cout << "ccnative dir <ccDir-compatible arguments>\n";
    std::cout << "ccnative cc <cc-compatible arguments>\n";
}

} // namespace

int main(int argc, char** argv) {
    try {
        if (argc < 2) {
            PrintHelp();
            return 2;
        }

        const std::string verb = ToLower(argv[1]);
        std::vector<std::string> args;
        for (int index = 2; index < argc; ++index) {
            args.emplace_back(argv[index]);
        }

        if (verb == "dir") {
            return RunDir(args);
        }
        if (verb == "cc") {
            return RunCc(args);
        }
        if (verb == "-h" || verb == "--help" || verb == "help") {
            PrintHelp();
            return 0;
        }

        std::cerr << "Unknown ccnative verb: " << argv[1] << "\n";
        PrintHelp();
        return 2;
    }
    catch (const std::exception& ex) {
        std::cerr << ex.what() << "\n";
        return 1;
    }
}
