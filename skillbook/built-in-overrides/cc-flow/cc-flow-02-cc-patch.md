---
title: CC Export + Patch
enabled: true
---
Input: CC source export plus optional user clarification.

If source is insufficient, output only the next narrow CC request list ending with END. Prefer exact file/FUNCTION lines; use exactly one FIND only when the missing owner cannot be named from visible source.

If source is sufficient, output one GO patch containing raw BEGIN/END CC-REPLACE blocks only. GO writes that raw output to patch.txt.

One GO patch may contain many CC-REPLACE blocks across many files. Do not split patches into separate chat answers by file.

Do not emit prose mixed with the patch, git diff, shell commands, apply_patch syntax, direct file edits, markdown fences, or commentary inside the patch.

Supported MODE values: replace_region, insert_include, whole_file, insert_after_function, insert_before_function, delete_function, function, append_to_file, create_directory.

General rules:
Use FILE for file-targeting modes.
Use DIR only for create_directory.
Body modes require --- followed by replacement text.
Bodyless modes are insert_include, delete_function, and create_directory.
function, insert_before_function, insert_after_function, delete_function, and replace_region require NAME.
insert_include requires HEADER.
whole_file body must be the complete final file content.
New files use MODE: whole_file.
If adding, removing, or renaming C++ source files, include the required CMakeLists.txt/build-file CC-REPLACE block in the same GO patch.
Ground every edit in visible source and choose the least invasive valid ccReplace mode.
If a required target, declaration, dependency, or build owner is not visible, request the next narrow CC export instead of guessing.
Do not ask for more context when the visible export already contains the target file and enough surrounding source to write the edit.

Mode choice order:
1. replace_region: use for visible CC-REPLACE-BEGIN/END markers.
2. insert_include: use for one missing C/C++ include.
3. whole_file: use for new files, small files, unmarked files, or risky structure edits.
4. insert_after_function / insert_before_function / delete_function: use around one unique visible function.
5. function: use only when replacing one unique unambiguous function.
6. append_to_file: use only for additive tail content.
7. create_directory: use before creating files inside a new folder.

Patch block skeleton:
BEGIN CC-REPLACE
FILE: path/relative/to/project_root.cpp
MODE: whole_file
---
replacement text
END CC-REPLACE

Header variants: create_directory uses DIR instead of FILE; insert_include adds HEADER and has no body; function/insert_before_function/insert_after_function/delete_function/replace_region require NAME; bodyless modes omit ---.
