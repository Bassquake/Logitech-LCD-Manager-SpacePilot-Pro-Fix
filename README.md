# LcdLabelHelper

Brings back the button labels on the **3Dconnexion SpacePilot Pro** LCD ("Function Keys" screen) under 3DxWare 10.8 and later using a very tiny exe! May work for other 3DConnexion devices.

![Yay!](https://github.com/Bassquake/Logitech-LCD-Manager-SpacePilot-Pro-Fix/blob/master/assets/spacepilot-pro-text-yes.jpg)

## The problem

From 3DxWare **10.8.5** onwards (tested up to **10.9.14**), the SpacePilot Pro's Function Keys screen shows no button labels. 3DxWare **10.7.4** and earlier work fine.

![No text on screen](https://github.com/Bassquake/Logitech-LCD-Manager-SpacePilot-Pro-Fix/blob/master/assets/spacepilot-pro-text-none.jpg)

This isn't Windows blocking anything, and it isn't caused by the LCD applets (`3DxLcdApplets64.msi` is byte-for-byte identical across these versions). The cause is in the driver service:

- The LCD applet (`3DxLCD10SPPRO.exe`) draws each button's label from `%LOCALAPPDATA%\3Dconnexion\3DxWare\Cfg\LCDCfg.xml`, which `3DxService.exe` writes.
- `3DxService` 17.7 (3DxWare 10.7.4) writes a `<Text>` label inside every `<Button>`.
- `3DxService` 17.8 (10.8.5) and 17.9 (10.9.14) no longer write that `<Text>`. The code that wrote it was removed. The applet still runs, but it has nothing to draw.

## What this does

LcdLabelHelper runs quietly in the background. Whenever the driver rewrites `LCDCfg.xml`, the helper:

1. reads which profile, per-app customisations and button bank are active, from the driver's own status file (`3DxServiceState.xml`);
2. looks up each button's assigned action the same way the driver does, through your customisations, the app's profile, the shared and default profiles, and the base profile;
3. looks up the action's display name in your language, using the driver's own translation files;
4. inserts the missing `<Text>` labels, leaving everything else in the file exactly as the driver wrote it.

The applet notices the change and redraws the screen with labels. The labels follow the active application and include keys you've customised.

The helper only adds labels to the SpacePilot Pro section of the file. Other devices are left untouched. On driver versions that still write labels, it does nothing.

## Requirements

- Windows 10 or 11 (uses .NET Framework 4, which is built in)
- 3DxWare 10.8 or later, with a SpacePilot Pro
- No administrator rights needed

## Usage

Download `LcdLabelHelper.exe` from the Releases page and put it anywhere, for example `%LOCALAPPDATA%\Programs\LcdLabelHelper\`. Its version is shown in the exe's Properties → Details.

| Command | What it does |
|---|---|
| `LcdLabelHelper.exe` | Starts the background watcher. Only one copy runs at a time; starting it again while it's running does nothing. |
| `LcdLabelHelper.exe --once` | Patches `LCDCfg.xml` once and prints the version and each button's resolved label. |
| `LcdLabelHelper.exe --exit` | Stops the running background copy. |

Any other argument is ignored, and the background watcher starts.

The exe has no window, so a console doesn't flash at startup. A side effect is that PowerShell doesn't wait for it, and the `--once` output can overlap your prompt. Pipe it to get clean output:

```powershell
.\LcdLabelHelper.exe --once | Out-String
```

The log is at `%LOCALAPPDATA%\3Dconnexion\3DxWare\LcdLabelHelper.log`, and records which version started.

### Start automatically at sign-in

Press <kbd>Win</kbd>+<kbd>R</kbd>, enter `shell:startup`, and create a shortcut to `LcdLabelHelper.exe` there with no arguments. Don't run it as administrator: it works on your own user profile's files.

### Updating LcdLabelHelper

Run `LcdLabelHelper.exe --exit` first, because Windows won't let you replace an exe that's running. Then overwrite it with the new `LcdLabelHelper.exe` and start it again. Your startup shortcut keeps working, since the filename doesn't change.

### Uninstall

Run `LcdLabelHelper.exe --exit`, remove the startup shortcut if you made one, and delete the exe. Optionally delete the log file. The helper changes nothing else; the driver rewrites `LCDCfg.xml` on its own the next time the active app changes.

## Upgrading 3DxWare

Before upgrading the driver, stop the helper (`--exit`) and back up `%APPDATA%\3Dconnexion\3DxWare\Cfg`, which holds your customised keys. Afterwards, start the helper again and run `--once` to check the labels.

3DxWare 10.9 changed the profile file layout compared with 10.8. A future version could change it again; if labels go missing after an upgrade, open an issue with the `--once` output.

## Building from source

The source is a single file and builds with the C# compiler included in Windows; no SDK or Visual Studio needed:

```bat
build.cmd
```

which runs:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -optimize -out:LcdLabelHelper.exe LcdLabelHelper.cs
```

The version in the exe's Properties comes from the `public const string Version` line in `LcdLabelHelper.cs`. To release a new version, change that one line and rebuild.

The release exe isn't code-signed, so Windows SmartScreen or antivirus software may warn about it. If you'd rather not trust a downloaded binary, read `LcdLabelHelper.cs` and build it yourself.

## Limitations

- Tested on one machine with 3DxWare 10.8.5 and 10.9.14.
- In 10.9, an app's own button assignment is taken to override the new shared assignments. That matches how app overrides behave in 10.8, but wasn't confirmed against the driver at runtime. If a label disagrees with the 3Dconnexion Buttons screen, please report it.
- The label is the action's name as shown in 3Dconnexion's own button settings.

## Disclaimer

Not affiliated with or endorsed by 3Dconnexion. 3Dconnexion, SpacePilot and 3DxWare are trademarks of their respective owners. This tool only reads 3DxWare's configuration files and adds labels to the LCD file the driver already writes; it doesn't include, modify or redistribute any 3Dconnexion software.

## License

[MIT](LICENSE)
