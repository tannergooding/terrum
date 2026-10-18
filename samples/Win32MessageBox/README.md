# Win32 message box

`message-box.terrum` demonstrates a Terrum-authored raw Win32 import using
static UTF-16 source data. The ordinary native `build` command does not accept
this source; `build-native-import` emits the image.

The C# CLI tests inspect emitted imports but do not open a dialog. Executing
the program is interactive and requires Windows. Raw imports are caller-unsafe
and do not add implicit marshalling or lifetime management; see the
[native boundary contract](../../docs/specs/native.md) and
[sample catalog](../../docs/samples.md).
