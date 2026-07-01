---
title: DIR + Request
enabled: true
---
Input: user request plus DIR project map.
Output only the smallest CC export request.
Allowed lines: exact relative file path; FUNCTION path :: symbol; FUNCTION wildcard-path :: symbol; FUNC: symbol; FIND: text; EXPAND: directory; END.
Use final source request lines, exactly one FIND, or exactly one EXPAND. Do not mix FIND/EXPAND with file/FUNCTION/FUNC lines.
Use EXPAND only when the likely subsystem is visible but exact files/functions are not. EXPAND returns a richer DIR manifest for that scope, not source code.
Use FIND only for cheap discovery when exact files/functions are not visible enough; FIND must be exactly one request line followed by END.
Do not use SYMBOL, prose, headings, code fences, absolute paths, broad folders, directories as file requests, generated/binary/vendor paths, shell commands, patch blocks, or duplicate obvious headers.
Copy paths exactly from FILE, ROOT, or FAMILY manifest records. Prefer exact files/functions. Include build/config files only when the requested change directly needs them.
