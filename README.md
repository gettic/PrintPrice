<p align="center"><img src="assets/logo.png" width="128" alt="Logo"></p>

# Gettic's 3D Print Price Calculator

> [!NOTE]
> **AI disclaimer:** This app, including its code, logo and documentation, was made with the help of AI. It has been tested, but it may still contain mistakes. The prices it calculates are estimates, so check them before you send a quote.

A small dark-mode Windows app that turns a 3D print job into two price quotes: one for **friends & family** and one for **customers**. It's made for the **Bambu Lab A1** and Danish prices (DKK).

<p align="center"><img src="assets/screenshot.png" width="480" alt="Screenshot"></p>

<p align="center"><img src="assets/screenshot-multicolour.png" width="480" alt="Screenshot of a multi-colour print"></p>

## Download
Download **`PrintPrice.zip`** from the [latest release](../../releases/latest), unzip it and run **`PrintPrice.exe`**. Nothing needs to be installed: Windows 10/11 already includes what it needs (.NET Framework 4.8).

The exe isn't code-signed, so the first time you run it Windows SmartScreen may warn you. Click **More info → Run anyway**.

**Optional:** install [Bambu Studio](https://bambulab.com/en/download/studio) to detect print time and filament use automatically.

## Features
- **Print time & filament from a link:** paste a **Printables** link and the app downloads the model and slices it in Bambu Studio with A1 profiles.
  - **Other sites** (MakerWorld, Thingiverse…) block automatic downloads. Download the STL/3MF yourself and drop it on the window instead.
  - **Already-sliced `.gcode.3mf` files** are read instantly, without slicing again.
  - **Multi-colour prints:** if the model uses 2 or more filaments, the Cost breakdown shows one row per colour with its own kr/kg box. A line below shows the purge waste and the time spent on colour changes (for example "incl. 124 g purge · 8,7 h colour changes"). With more than 4 colours the rest are grouped in one "+N more" row. If you pick several model files, they are added together. **Filament used** becomes the read-only total.
  - **Empty link:** if you press **Get time & weight** with an empty link box, a red "Paste a link or load a file first" hint shows in the box. After 10 seconds the grey example link comes back. Pressing Enter in the empty box loads the example Benchy link and gets its time and weight.
  - **Model picture:** a small picture of the model shows under the Model link: the Printables photo, or the picture inside a `.3mf`. After slicing, it switches to Bambu's render of the plate. Without a model it shows the logo.
- **Live electricity price:** the current 15-minute spot price for DK1/DK2 comes from Energinet's free [Energi Data Service](https://www.energidataservice.dk/). The app adds your tariffs, taxes and fees, then 25 % VAT.
- **Wear & tear:** a machine-time rate is worked out from the chosen **nozzle**, **filament** and **build plate**. It is added to both prices.
  - **Abrasive filaments:** the app knows which filaments are abrasive (CF, GF, Glow…) and warns if one is used on a stainless steel nozzle.
- **Failed prints:** optional. Set a % in Settings and material and power are raised by it for failed prints and reprints, before the markup. The default is 0 % (off). When it is above 0, the Cost breakdown shows a "Failed prints" line.
- **Configurable:** set the markups, minimum price, rounding, failed prints, printer power, print profile, price area and language in Settings.
- **English or Danish:** under **Sprog / Language** in Settings choose Automatic (Danish if Windows is Danish, otherwise English), English or Dansk. It takes effect after a restart; **Restart now** does it at once. The whole program and the copied quote are translated.
- **Copy quote:** puts a ready-to-send text on the clipboard and saves the quote in the price history.
- **Price history:** **Save** also saves the quote (the status line shows "Saved to history."). **History…** (next to **Load file…**) lists the saved quotes.
  - You can search and filter the list. One **Price** column shows the friends & family or the customer price, following each row's **For** choice (Customer by default).
  - **For** and **Note** can be edited in the list and are saved at once. **Delete** asks for a second click.
  - **Open again** (or double-click, or Enter) puts the inputs back and recalculates with today's settings. The model box shows the saved price next to today's.
  - The same quote saved again within 10 minutes replaces the last one. The newest 500 quotes are kept.

<p align="center"><img src="assets/screenshot-history.png" width="480" alt="Screenshot of the History window"></p>

- **Windows:** every window can be dragged by any empty spot. All text (labels, info boxes, prices, status line) can be marked with the mouse and copied with Ctrl+C or right-click Copy. Double-click marks a word.
- **Auto-update:** at startup, and then every 10 minutes while the program is open, the app checks this repo's latest release. If there is a newer version, a red **Update to vX** button appears under the Friends & family price. Click it to see what's new in every version since yours (it scrolls if long), then choose **Update now** or **Later**. The download is verified against GitHub's SHA-256 checksum before it's installed. You can switch the check off in Settings (**Check for a new version at startup**); that switches off the 10-minute check too. Opening Settings also checks once (if the automatic check is on). Next to the version number in Settings, **check now** looks once when you click it. A newer version shows there and as the red update button.

## How prices are calculated
```
Material   = grams / 1000 × filament DKK/kg
Power      = hours × printer kW × electricity DKK/kWh
Base       = (Material + Power) × (1 + failed prints %)
Friends    = Base × (1 + friends markup) + hours × machine-time rate
Customers  = Base × (1 + customer markup) + hours × machine-time rate   (at least the minimum price)
```
The machine-time rate is added to both prices and is not marked up. The minimum price is for customers only. Both prices can optionally be rounded up to the nearest N kr. Failed prints are 0 % by default and do not affect the machine-time rate.

**Machine-time rate** (kr per print hour):

| Part | How it's calculated |
|---|---|
| Printer | 2.099 kr. ÷ 3.000 h |
| Parts and upkeep (PTFE tube, wiper, cutter, belts, extruder gears, rods/bearings, grease, IPA, glue stick, shipping) | 600 kr. ÷ 1.500 h |
| Hotend | 139 kr. stainless (500 h; ~100 h with abrasive filament) or 185 kr. hardened steel (750 h; 300 h abrasive) |
| Build plate | price ÷ life; PETG/TPU halve the smooth PEI plate's life |

Plate prices and lives: Smooth PEI 200 kr. / 600 h, Textured PEI 150 kr. / 1.000 h, SuperTack 260 kr. / 500 h, Cool 150 kr. / 500 h, Engineering 180 kr. / 800 h.

The default setup (0.4 stainless nozzle, PLA, smooth PEI plate) comes to **1,71 kr/h**. All of these numbers are estimates and live in `src/Pricing.cs`.

## Defaults
| Setting | Default |
|---|---|
| Printer power | 100 W (A1 average when printing PLA) |
| Markups | 25 % friends & family, 75 % customers |
| Failed prints | 0 % (off; % of material + power) |
| Language | Automatic (Danish if Windows is Danish, otherwise English) |
| Slicing | 0.4 mm nozzle, 0.16mm Optimal, Bambu PLA Matte, Smooth PEI plate |
| Electricity | DK1, 0,55 kr/kWh fees on top of spot, 25 % VAT |

Settings are saved in `%APPDATA%\PrintPrice\settings.ini`.

## Privacy
The app sends no personal information anywhere. It has no account, cookies or tracking. It only connects to these services:

| Service | When | What is sent |
|---|---|---|
| GitHub | At startup, every 10 minutes while the program is open, and when you open Settings, to look for a new version (all of it can be switched off in Settings); when you click **Update now** | A normal web request with the app name and version. Unchanged answers are not downloaded again (ETag), and if GitHub says "too many requests" the app quietly waits longer |
| Energi Data Service | At startup to get the electricity price (can be switched off in Settings), or when you click the price button | The price area (DK1/DK2) |
| Printables | Only when you paste a Printables link | The model's ID, then the file and a small model picture (~10 KB, from media.printables.com) download |

Like any web request, these show your IP address to the service. Local `.3mf` files and sliced results use the picture stored inside the file, with no network. Bambu Studio runs on your own PC; the app only starts its command-line slicer.

Saved quotes (price history) are kept only on this PC in `%APPDATA%\PrintPrice\history.json` and are never sent anywhere. Delete that file to clear the history.

Nothing is installed. The app writes only its settings file, the price history and temporary slicing files. To uninstall, delete PrintPrice.exe and the folder %APPDATA%\PrintPrice.

## Build from source
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```
This runs the tests and compiles `bin\PrintPrice.exe` with the C# compiler that ships with Windows, so no SDK or Visual Studio is needed. To regenerate the logo and icon, run `tools\make-icon.ps1`. The switch on its first line picks the AI logo (`'ai'`, made from `assets\logo-master.png`) or the old drawn one (`'drawn'`); line 2 sets the outline colour.

To redraw the README pictures, run `PrintPrice.exe --screenshots <folder>`. This hidden option saves the windows as PNGs in that folder and exits. It uses default settings, never goes online and never saves anything. Add `--lang da` for Danish pictures. A `model.png` in the folder is used as the sample model picture for `main.png`, and a `model2.png` for the multi-colour `main_multi.png`.

For testing, `PrintPrice.exe --settings "<folder>" [--hidden | --minimized]` runs the app on a separate settings folder. The folder is created if missing and starts with defaults. The real `%APPDATA%\PrintPrice` is never touched, and there are no update checks or installs. `--hidden` keeps the window off-screen.

### Project layout
| Path | What |
|---|---|
| `src/Pricing.cs` | Price formula and wear model |
| `src/Slicer.cs` | Bambu Studio command-line slicing; merges profile inheritance, which the CLI doesn't do |
| `src/Printables.cs` | Printables API: model files and downloads |
| `src/PowerPrice.cs` | Energi Data Service spot prices |
| `src/Updater.cs` | Update check and install from GitHub releases |
| `src/History.cs` | Saved quotes (price history) |
| `src/HistoryForm.cs` | The History window |
| `src/MainForm.cs`, `src/SettingsForm.cs`, `src/UpdateForm.cs`, `src/Theme.cs` | User interface |
| `src/Lang.cs` | English and Danish texts |
| `src/Screenshots.cs` | Hidden `--screenshots` picture mode |
| `tests/PricingTests.cs` | Unit tests |
| `.github/workflows/build.yml` | GitHub Actions: builds and tests the release exe |
| `LICENSE` | GPL-3.0 |

## License
[GPL-3.0](LICENSE). Copyright (C) 2026 Gettic.
