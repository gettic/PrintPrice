<p align="center"><img src="assets/logo.png" width="128" alt="Logo"></p>

# Gettic's 3D Print Price Calculator

> [!NOTE]
> **AI disclaimer:** This app, including its code, logo and documentation, was made with the help of AI. It has been tested, but it may still contain mistakes. The prices it calculates are estimates, so check them before you send a quote.

> [!TIP]
> **Try it in your browser, no install:** [gettic.github.io/PrintPrice](https://gettic.github.io/PrintPrice/)

A small dark-mode Windows app that turns a 3D print job into two price quotes: one for **friends & family** and one for **customers**. It's made for the **Bambu Lab A1**, **A1 mini** and **P1S**, and Danish prices (DKK).

<p align="center"><img src="assets/screenshot-multicolour.png" width="480" alt="Screenshot"></p>

## Download
Download **`PrintPrice.zip`** from the [latest release](../../releases/latest), unzip it and run **`PrintPrice.exe`**. Nothing needs to be installed: Windows 10/11 already includes what it needs (.NET Framework 4.8).

The exe isn't code-signed, so the first time you run it Windows SmartScreen may warn you. Click **More info → Run anyway**.

**Optional:** install [Bambu Studio](https://bambulab.com/en/download/studio) to detect print time and filament use automatically.

<a id="first-start"></a>
**First start:** on a new PC, a short setup window opens before the main window. It has 4 steps and a summary:

| Step | What you set |
|---|---|
| 1 | Language (English or Dansk). **Restore a backup…** if you used PrintPrice before |
| 2 | Which printers you have (A1, A1 mini, P1S; tick each one), and whether Bambu Studio was found |
| 3 | Your filament price, and a live (DK1 or DK2) or fixed electricity price |
| 4 | Markups, minimum price and your name on quotes, with an example price |

- **Back**, **Skip** and **Next** are on every step. **Skip** keeps the default for that step.
- If Bambu Studio isn't found, step 2 has a **Download Bambu Studio** link and **Find bambu-studio.exe…**.
- A restored backup with settings ends the setup. A backup with only the price history lets you carry on.
- **Set up later** (or closing the window) keeps the defaults. The main window then shows "Setup skipped – run it now" once.
- Run it again any time from **Settings → App → Run setup again…**. It fills in your answers, and only adds printers, never removes them. A new language takes effect after a restart (PrintPrice restarts when you save).
- If you already use PrintPrice, you never see it after an update.

<p align="center"><img src="assets/screenshot-setup.png" width="420" alt="Screenshot of the setup window"></p>

## Website
There is also a calculator in the browser: [gettic.github.io/PrintPrice](https://gettic.github.io/PrintPrice/). Type the print time, grams, kr/kg and the electricity price, and you get both prices and the cost breakdown. It is in English and Danish. Settings are kept only in that browser.

**First visit:** a brand-new visitor gets the same short setup as the app's [first start](#first-start): 4 steps and a summary.
- Step 2 is one printer tile (A1, A1 mini or P1S), with its power and wear per hour. There is no Bambu Studio check.
- The language switches the page at once. **Restore a backup…** works like in the app: a backup with settings ends the setup.
- **Set up later** (or ✕ or Esc) keeps the defaults, and the page shows "Setup skipped – run it now" once.
- Run it again from **Settings → App → Run setup again…**, or open the address `gettic.github.io/PrintPrice/#setup`. A new printer there changes the printer's model, like the **Model** box in **Settings → Printer**.
- If you already use the site, you never see it.

**Size:** the first visit downloads about 3.2 MB (gzip) or 2.6 MB (brotli, used when the browser allows). The browser unpacks the brotli files with Google's small Brotli decoder, included with the site. The decoder runs in your browser and sends nothing.

**Sliced files:** drop a sliced `.gcode.3mf` or a G-code file on the page, or choose one. G-code from Bambu Studio, OrcaSlicer, PrusaSlicer (and SuperSlicer) and Cura can be read. It fills in the print time, the grams, one row per colour (the kr/kg of each filament is remembered) and the plate picture.
- The file is read in your browser only and is never uploaded. Big files are read in parts.
- A sliced `.3mf` with 2 or more plates shows one line per plate under the file line, for example "Plate 2 'Wheels' – 1 h 20 m, 34 g, 3 objects". The price is for all the plates together. After 8 plates the rest show as "+N more".
- An unsliced STL or 3MF gets a message to slice it first and export the sliced plate.
- A file from another slicer than Bambu Studio shows a muted note that the time is that slicer's estimate. Grams worked out from the filament length are marked with "≈". Cura files give no colours.
- PrusaSlicer's binary `.bgcode` can't be read. The message says to export plain `.gcode`.
- G-code made for a printer that isn't a Bambu Lab one is accepted, with a warning that the time is for that printer.
- A file with only the time or only the weight fills in what it has. The other box stays empty, with "Couldn't find the … in the file: type it in.", and there is no price until it's typed.
- A file with impossible numbers (over 30 days or 20 kg) is refused.
- A file sliced for another of your printers shows a warning and a button to switch printer. Unsupported printers are refused, like in the app.
- The prime tower grams of a file are shown under the colour rows ("Prime tower 1,7 g"), and the colour-change grams then leave the tower out. **Settings → Quote & invoice** has the same **Show prime tower grams on quotes and invoices** tick (off by default) for quotes, invoices and combined bills. The site can't slice, so it has no Prime tower On/Off choice. Big G-code files use much less memory for the tower count and are read faster.

**Live electricity price:** under **Electricity price**, the **Update** button fetches the current Danish price, with a line that shows what it is made of. **Settings → Electricity** has the price area (DK1 or DK2), tariffs and fees, 25 % VAT, and **Fetch the live price when the page opens**. If the price service is busy or can't be reached, the page keeps the price you typed.
- It always uses the price for the current time slot, for example "Live DK1 15:00".
- If only a saved copy is available, or the prices were fetched over about a day ago, the line adds "(price from <time or date>)".
- Prices too old to cover the current hour are not used. The line says how old they are, and your own price stays.
- The relay gets the prices from Energi Data Service, and from [elprisenligenu.dk](https://www.elprisenligenu.dk) when the first one doesn't answer. Then a small "Prices: elprisenligenu.dk" credit with a link shows under the line.

**History:** the **History** tab is the price history of the Windows app in the browser.
- **Save to history** on the calculator saves the quote. The tab has the search, the **Month**, **Show** and **Printer** filters and the month cards. In the list you can change **For**, **Note**, the print **Status**, **Delete**, **Open again** and **Repeat print**. Failed prints pay themselves back, like in the app.
- A read-only **Customer** column shows who a quote or invoice is made out to (hidden on narrow windows, shown on phone cards and Large tiles). A quote that is printing now shows an amber "Printing, done about 16:40" clock, like the app.
- If the browser's storage is full, the History page offers **Download a backup** and **Delete safety copies** (two clicks) instead of only an error.
- **Delete several:** tick quotes (or **Tick all shown**) and click **Delete 3 ticked…** (with your number). On a phone, **Select** turns on the tick boxes. **Clear all…** (next to **Backup…**) deletes every quote. It keeps a safety copy in the browser first, with the pictures (the newest 3 are kept), and offers **Download**. Both ask first, and **Undo** brings the quotes back until the page is reloaded.
- **Print status:** one **Status** column (Not printed yet, Printing now, Printed, Failed) with coloured marks, only the mark on narrower windows. Click it to open a small menu that saves at once. Ticked quotes get a **Print status ▾** button. On a phone each card has a status chip that opens the choices at the bottom of the screen. The **Show** filter lists the four statuses. Setting 2 or more prints to Failed asks first.
- **Views:** **List**, **Medium** or **Large** pictures, remembered in the settings.
- On the site there is no slicing, so **Repeat print** uses the saved time and grams with today's prices. Drop the sliced file again to update them.
- Each saved job keeps a small picture (256 px). The list shows it (bigger when you point at it), and so do Open again, Repeat print, Quote… and Invoice…. A job without a picture shows a cube.
- The history is kept in this browser only, in the same `history.json` format as the desktop app. Clearing the browser's site data deletes it. Safari can delete a site's storage after 7 days without a visit.

**Backup and restore:** **Backup…** on the History tab opens the Backup window, like the app's.
- **Back up** saves a `.printprice-backup` file that the Windows app can restore too. It is not locked with a code and holds customer names and payment details, so keep it private. Or back up **online** with the same 20-character code as in the app. The code is typed each time and the site never remembers it. The backup is locked in your browser before it is sent.
- **Restore** takes a `.printprice-backup` (from the site or the app), the app's `history.json`, or an online code. Choose **Merge** or **Replace**, and optionally **Also restore the settings**. A safety copy of what was there is kept first, and you can download it afterwards.
- Backups hold each job's small picture too, both ways with the app. A file backup takes the newest pictures first, up to 20 MB, and an online backup the newest that fit. **Merge** keeps pictures already in the browser, **Replace** uses the backup's. The safety copy includes the pictures, and Restore shows how many pictures a backup has.
- A **Last backup** line under the list reminds you to back up. It turns into a warning if you never backed up or the last backup is over 30 days old.

**Quote and invoice:** **Show quote…** on the calculator, and **Quote…** and **Invoice…** on the History tab, open the A4 page, like the app's. A saved quote shows the same rows as when it was saved.
- **On a phone** the quote or invoice opens as an easy-to-read page, one column. **Show the A4 page** switches to the printed page, and **Show for reading** switches back.
- A scaled job shows its scale in History and on quotes and invoices, for example "3D Benchy (150 × 100 × 50 %)".
- Type the customer's name, choose the **Customers** or **Friends & family** price, and choose a **Dark** or **Light** look.
- **Print / PDF…** opens the browser's print window. Choose "Save as PDF" there. It is always one light A4 page. **Copy text** copies the quote as text. **Save to history** saves it.
- An invoice needs the customer's name. It gets its random invoice number (for example 2026-48213) when it is made, and ticks **Printed**. After that it keeps its number and the customer's name (the name box is then read-only). A made invoice's price is locked ("Price on the invoice (locked)"), and saving its quote from History doesn't change it. In History, **For** can't be changed on invoiced rows. A single-print invoice shows "Bill to" as its top details line.
- **Print status:** the quote and invoice windows always have the box: Not printed yet, Printing now, Printed or Failed. On a new quote, every **Print / PDF…**, **Copy text** or **Save to history** saves it. A new invoice starts at Printed. In a saved quote or invoice a change is saved at once ("✓ Print status saved: Printed."). Printing now starts History's clock. Failed marks the quote as failed. A combined bill has one box for all its prints ("Mixed" when they differ). **Invoice…** on a print marked as failed says "This print is marked as failed in the history."
- **Show invoice…:** with **Calculator makes invoices, not quotes** on (Settings → Quote & invoice, off by default), the calculator's button makes an invoice for the job on screen. A name is required. The first **Print / PDF…** or **Copy text** gives it a number, saves it in History and ticks **Printed**. More prints from the same window keep that number. **Save to history** saves a quote without a number.
- **Settings → Quote & invoice** has the name on the quote, **Valid for**, **Pay within**, MobilePay and bank account. MobilePay and bank account show on invoices only when filled in.
- **Settings → Reset to defaults** (the old "Defaults" button) asks first and keeps your name, payment details and language, and your printers and invoice numbers.
- **Several prints on one bill:** in History, tick 2 or more quotes (on a phone: **Select**) and click **Quote…** or **Invoice…**. The page works like the app's [one bill for several prints](#one-bill-for-several-prints): a card per print, **Bill to** and **Prints** on top, the printer and build plate beside **Price breakdown**, one breakdown and the total. Each print's materials are listed under its name, with its own prime tower line when that is switched on. An invoice gives all of them one number and marks them printed. Failed prints are left off, a mix of billed and unbilled prints is refused, and **Invoice…** on one print of a combined invoice shows the whole bill. Long bills get real A4 pages, as in the app.
- **Paid:** History's Invoice column has a "✓ Paid" / "Unpaid" button next to the number. It marks every print on that invoice (on a phone: "Mark as paid / unpaid" in the card's actions). **Show** has "Unpaid invoices", which adds a line like "Unpaid: 2 invoices · 150,00 kr." (and how many are overdue). A paid invoice's page says "Paid 5 November 2026 – thank you" instead of "Please pay by …" and hides the payment details (MobilePay, bank, "Please write invoice no. …"), also on combined bills when all prints are paid.
- Invoice PDFs are suggested as "2026-48213 - Name". Quotes keep "date model - name".

It does less than the Windows app: no slicing.

The site runs in your browser. It stores your settings and price history in the browser's own storage. Files you drop on it are read on your PC and never leave it. The only link you click is **GitHub** at the bottom, and nothing is sent before you click it.

The one outside service the site calls is PrintPrice's small Cloudflare relay (`printprice.gtd-relay.workers.dev`). It is used for the live electricity price, where it is sent the price area (DK1 or DK2) and nothing else, and for online backup, and only when you use it. For online backup it gets only the locked copy and a storage id made from the code, never the code. Cloudflare sees your IP address, like any website does, and the relay keeps only a salted hash of it, as described under Privacy. The setup's electricity step asks too, while the live price is chosen. Turn the live price off in **Settings → Electricity**: then the page only asks when you click **Update**.

## Linux and Steam Deck
The Windows app isn't tested under Wine or Proton. Use the website instead, in any browser: [gettic.github.io/PrintPrice](https://gettic.github.io/PrintPrice/). To fill in time and grams from a model, slice it in Bambu Studio for Linux (Flathub `com.bambulab.BambuStudio`, or the AppImage) or in OrcaSlicer, PrusaSlicer or Cura, export the sliced plate, and drop the `.gcode.3mf` or `.gcode` file on the site.

## Features
- **Print time & filament from a link:** paste a **Printables** link and the app downloads the model and slices it in Bambu Studio with the profiles of the printer in use.
  - **MakerWorld** needs you to sign in to download. A MakerWorld link shows how: open the page, click **Download (3MF)**, then drop the file on the window or use **Load file…**. Colours and plates are read from the file. **Open the page** opens the link in your browser.
  - **Other sites** (Thingiverse, Cults3D…) block automatic downloads. Download the STL/3MF yourself and drop it on the window instead.
  - **Several files and colours:** pick 2 or more files on a Printables model, or drop or load 2 or more model files, and the **Choose files and colours** window opens.
    - Tick the files to print. Each plain model file gets a filament and a colour: click the colour swatch for a menu of 13 filament colours. By default every file gets the filament chosen in Settings.
    - Files with the same filament and colour count as one colour row. A `.3mf` or G-code keeps the colours in the file ("colours from the file").
    - **Print together on one plate (AMS lite)** is off by default. It needs 2–4 different colours. It is much slower and uses more filament for colour changes: in tests about 4× the time and 3.7× the filament.
    - If the files don't fit on one plate together, or slicing them together fails, each file is sliced on its own plate instead. A yellow note says "Sliced one by one instead.", with the reason in its tooltip.
  - **Plates:** when the price covers 2 or more plates (a file with several plates, or several files sliced one by one), a link under the result says "The price is for both plates – show them" (or "all N plates" for 3 or more). It opens a list with one line per plate: name, time in h and m, grams and the number of objects. Point at a line to see the object names.
  - **Already-sliced `.gcode.3mf` files** are read instantly, without slicing again. A file sliced for another of the supported printers is priced, with a warning.
  - **G-code from other slicers:** drop a `.gcode` on the window, or pick it with **Load file…** (one G-code at a time). Files from Bambu Studio, OrcaSlicer, PrusaSlicer (and SuperSlicer) and Cura are read for print time, grams, and colours and filament types where the file has them. Cura gives no colours.
    - A file from another slicer than Bambu Studio shows a grey note that the time is that slicer's estimate. Grams worked out from the filament length show as an estimate, for example "≈ 11,7 g".
    - A G-code made for a printer that isn't a Bambu Lab one is priced, with a warning that its time is for that printer.
    - PrusaSlicer's binary `.bgcode` isn't read. The message says to export plain `.gcode`.
    - A file with only the time or only the weight fills in what it has. The other box stays empty, with "Couldn't find the … in the file: type it in.", and there is no price until it's typed. A file with impossible numbers (over 30 days or 20 kg) is refused.
    - A dropped or loaded file shows its name in the **Model link** box. The full path is in the tooltip.
  - **Printers PrintPrice doesn't support yet:** a sliced file made for another printer (for example X1 Carbon or H2D) is not priced. You can ask for your printer to be added by opening an issue at [github.com/gettic/PrintPrice/issues](https://github.com/gettic/PrintPrice/issues). The message has the link.
  - **Model too big:** if the model doesn't fit the printer (or a plate in the file is empty), the message says so and shows the build volume: A1 256 × 256 × 256 mm, A1 mini 180 × 180 × 180 mm, P1S 256 × 256 × 250 mm. It ends "Pick a bigger printer, or use Scale… to make it smaller."
  - **Plate and filament:** if Bambu Studio won't print a filament on the chosen build plate, the message names the filament and the plate. Pick another plate in Settings > Printer, or another filament.
  - **Multi-colour prints:** if the model uses 2 or more filaments, the Cost breakdown shows one row per colour with its own kr/kg box. A line below shows the purge waste and the time spent on colour changes (for example "incl. 124 g purge · 8,7 h colour changes"). With more than 4 colours the rest are grouped in one "+N more" row. Colours for which the sliced file names no real filament (empty, or just generic PLA) get the filament chosen in Settings (default Bambu PLA Matte). Filaments the file names are kept. Several model files are added together (see Several files and colours above). **Filament used** becomes the read-only total.
  - **Prime tower:** Bambu Studio prints a small tower that cleans the nozzle at colour changes. For 2 or more colours it is on by default, also for two-colour projects that use the same filament twice. The breakdown note shows its share, for example "prime tower 1,7 g · 5 min". A 2+ colour job has a **Prime tower: on** (or **off**) link under its result. Click it to slice again with the other choice (off saves filament and time but can leave colour smudges). Set the default in **Settings → Quote & invoice → Slicing**. The job remembers its choice and its tower grams.
  - **Tower grams on quotes:** the grams are already in the material price. With **Show prime tower grams on quotes and invoices** ticked (off by default), quotes, invoices and copied text add a note, for example "incl. 1,7 g for the prime tower". It has no price of its own.
  - **Empty link:** if you press **Get time & weight** with an empty link box, a red "Paste a link or load a file first" hint shows in the box. After 10 seconds the grey example link comes back. The empty box says "Paste a Printables link, or press Enter for an example". Pressing Enter there slices a 4-colour Benchy straight away, without the file picker, and gets its time and weight, so you can see the multi-colour rows.
  - **Model picture:** a small picture of the model shows under the Model link: the Printables photo, or the picture inside a `.3mf`. After slicing, it switches to Bambu's render of the plate. Without a model it shows the logo.
- **Scale model:** after **Get time & weight**, a **Scale…** link under the result opens the **Scale model** window.
  - The window has a **%** box and a **mm** box for X (width), Y (depth) and Z (height). Each size is from 10 to 500 %. Typing a size in mm sets that axis's %. The mm sizes come from the last slice.
  - **Same in every direction** is ticked at first: typing one value sets all three. Untick it to scale each direction on its own.
  - STL, OBJ and 3MF files can be scaled per direction. STEP and AMF files can only be scaled the same in every direction. The box stays ticked, with the note "… can only be scaled evenly".
  - The window warns "Too big for the Bambu Lab A1" (or your printer) when the model doesn't fit. Under 10 mm on the smallest side it shows a grey note, "Small prints can lose thin parts or fail to slice." (it never blocks). **Reset to 100 %** goes back, and **Apply (slices again)** slices at the new size.
  - If slicing fails at a small size, the result says "Too small at 20 % (…): the thinnest parts disappear when sliced. Try a bigger size." Other failures say "Bambu Studio could not slice <file>: parts of the model are too thin to print, or the model is damaged." If the model is sliced but doesn't fit on the build plate, it is refused with "The model doesn't fit on the … build plate …".
  - The scale is saved with the job and kept by **Open again** and **Repeat print**. A job scaled the same in every direction is shown as "3D Benchy (150 %)" on quotes, invoices, file names and in History. Different sizes per direction show as "150 × 100 × 50 %" (X × Y × Z).
  - G-code can't be scaled, so it has no link.

<p align="center"><img src="assets/screenshot-scale.png" width="360" alt="Screenshot of the Scale model window"></p>

- **Open in Studio:** after a successful slice, the **Get time & weight** button becomes **Open in Studio**. It opens the sliced file in Bambu Studio's Prepare tab with the same printer profiles. Click **Slice plate** there before you print. If you used several model files, the first one opens, or the one shared plate when they were printed together. An already-sliced `.gcode.3mf` opens your own file. In a multi-colour job Bambu Studio shows the filament names with a " #2" tag (for example "Bambu PLA Basic @BBL A1 #2"). The prime tower is kept. Editing the link, **Clear**, **Load file…**, opening a quote from History or changing the slicing settings turns the button back into **Get time & weight**. Enter in the link box does what the button shows.
- **Material rows with remembered prices:** the Cost breakdown starts with one material row per colour (one row for a one-colour print). Each row has a colour swatch, a drop-down with the full filament name (Bambu Studio's filament profiles for the printer in use), the grams, and a kr/kg box. Picking another material changes only that row and fills in its last kr/kg, with no new slicing. The app remembers one kr/kg price per filament (for example Bambu PLA Matte = 200). A filament you have never priced uses the last filament price. The filament chosen in Settings is the default for new slices.
- **Several printers:** add a Bambu Lab A1, A1 mini or P1S, as many as you like. Each printer has its own name, nozzle, print profile, build plate, power and wear rate. Markups, minimum price, failed prints, electricity and filament prices are the same for all printers.
  - **Printer card:** right of the print time and electricity rows, a card shows the printer in use and its power ("Bambu Lab A1 · 100 W"). The photo comes from your installed Bambu Studio, otherwise a drawn printer is shown.
  - **Choose printer:** click the card to open this window. It has a tile per printer with its picture, name, model, nozzle and plate, wear rate and power, and this month's prints and profit. Click a tile (or use the arrow keys and Enter) to use that printer for this quote and the next ones. With many printers the tiles scroll.
  - **Add printer…** in that window opens Settings to add one, and puts it in use. **Printer settings…** opens Settings > Printer.
  - **Changing printer after slicing:** a yellow note asks you to get time & weight again for the new printer.

<p align="center"><img src="assets/screenshot-printers.png" width="480" alt="Screenshot of the Choose printer window"></p>

- **Live electricity price:** the current 15-minute spot price for DK1/DK2 comes from Energinet's free [Energi Data Service](https://www.energidataservice.dk/). The app adds your tariffs, taxes and fees, then 25 % VAT.
- **Wear & tear:** a machine-time rate is worked out from the printer in use and the chosen **nozzle**, **filament** and **build plate**. It is added to both prices.
  - **Abrasive filaments:** the app knows which filaments are abrasive (CF, GF, Glow…) and warns if one is used on a stainless steel nozzle.
- **Failed prints:** optional. Set a % in Settings and material and power are raised by it for failed prints and reprints, before the markup. The default is 0 % (off). When it is above 0, the Cost breakdown shows a "Failed prints" line.
- **Failed prints payback:** on by default. In the price history, tick **Failed** on a saved quote. With **Earn back failed prints** on in Settings, new quotes get a payback of 10 % (**% per quote**) of material + power until the failed print's material, power and wear are made back. It never adds more than what is left. It is shown as **Failed prints payback** in the Cost breakdown, is not marked up, and is added after the minimum price and before rounding. **Paid by** is **Customers only** (default; only quotes saved For = Customer count) or **Both prices**. Settings shows **Not made back yet**. A saved quote counts as sold, so delete quotes you didn't sell.
- **Configurable:** Settings has five tabs: **Prices** (markups, minimum price, rounding, failed prints), **Printer** (which printer, name, nozzle, print profile, filament, build plate, power, wear rate), **Electricity** (price area, tariffs and fees, VAT, live price at start), **Quote & invoice** (name on the quote, valid days, **Slicing**: prime tower and its grams on quotes, invoice due days, MobilePay, bank, "Main window makes invoices, not quotes", keep a copy of every invoice PDF) and **App** (language, update check, Bambu Studio path, backup, and **Run setup again…**).
  - On the **Printer** tab, the **Printer** drop-down picks which printer you edit; the one in use is marked "(in use)". **Add…** adds an A1, A1 mini or P1S with that model's defaults. **Remove** removes the printer shown (only when you have 2 or more).
  - Each printer's **Name** is shown on the printer card and the quote. Left empty, the model name is used. The filament profile is the same for all printers.
- **English or Danish:** under **Sprog / Language** in Settings choose Automatic (Danish if Windows is Danish, otherwise English), English or Dansk. It takes effect after a restart; **Restart now** does it at once. The whole program and the copied quote are translated.
- **Quote page:** **Show quote…** opens a window with a live preview of an A4 quote page: logo, model picture, print time, weight, printer, material, a full price breakdown (material per colour, electricity, machine wear split into printer / spare parts / nozzle / build plate, "Work & handling", which also holds the failed prints payback and any minimum-price extra, and rounding when used), the total, a quote number, a valid-until date and small print.
  - **For:** an optional customer name (recent names from the history are offered).
  - **Price:** Customers or Friends & family.
  - **Look of the picture:** Dark (default) or Light. The PDF is always light.
  - **Print status:** Not printed yet, Printing now, Printed or Failed. It is saved with the quote on every export and on **Save to history**. A new job starts at Not printed yet. **Failed** works like setting **Failed** in History. The invoice window has the box too, and a new invoice starts at Printed. A status changed in a saved quote or invoice is saved at once. A combined bill has one box for all its prints ("Mixed" when they differ).
  - **Export:** **Copy picture**, **Save picture…**, **Save as PDF…** or **Copy text**. The **▾** on the button gives the same four as quick exports without a name.
  - **Save to history** saves the quote with the chosen price and name without making a file. Every export is also saved to the price history. Just opening the window saves nothing.
  - A bill never shows the minimum price as its own line. When the minimum price raises the customer price, the extra is part of "Work & handling".
  - The window is large and can be resized. Click the page to zoom to 100 %, and drag to move around.
  - The name on the quote, the number of days it is valid (default 14) and the printer name are set in Settings.
  - **Show invoice… instead:** with **Main window makes invoices, not quotes** on (Settings → Quote & invoice, off by default), the button makes an invoice for the job on screen. A name is required. The first copy or save gives it a number, saves it in History and ticks **Printed**. More copies from the same window keep that number. The **▾** quick exports still make quotes without a name (the website has no **▾** menu, but has this setting too).

<p align="center"><img src="assets/screenshot-quote.png" width="480" alt="Screenshot of a quote"></p>

- **Price history:** **Save to history** saves the quote (the status line shows "Saved to history."). **History…** (next to **Load file…**) lists the saved quotes.
  - **Month** picker (all months or one month, ‹ › to step) with six cards for that month: prints and failed prints, print time and filament, customers, friends & family, costs (material + power + wear) and profit with % of income.
  - **Printer:** every saved quote keeps its printer. Quotes saved before there were several printers count as your first printer. With 2 or more printers, the list gets a **Printer** column and a **Printer** filter. With one printer picked, the cards show that printer's totals (**All months** = all time). With **All printers**, the Profit card's tooltip lists the profit per printer. A removed printer is shown as "(removed)".
  - Profit per printer = price − material − power − that printer's wear. A failed print counts as a loss on its own printer.
  - You can search (model, customer name, link or note) and filter the list. One **Price** column shows the friends & family or the customer price, following each row's **For** choice (Customer by default). **Esc** in the search box clears it. The list updates shortly after you stop typing.
  - **For**, **Customer** and **Note** can be edited in the list and are saved at once.
  - **Customer:** the name from a quote or invoice. Type it in the cell like a note, or click the small ▾ in the cell for earlier names. In the right-click menu, **Customer name…** sets the name of all selected quotes, and **Select all quotes for <name>** selects that customer's quotes.
  - **Status:** one column with Not printed, Printing, Printed or Failed. Click it (or press Space) to change it. Quotes saved before print status existed count as printed. Failed quotes feed the payback (see Failed prints payback above). A quote set to **Printing** shows an amber clock (tooltip "Printing, done about 16:40") until its print time has passed. Then it counts as printed.
  - **Show** filter: All quotes, Friends & family, Customers, or one print status (Not printed yet, Printing now, Printed, Failed, under a "Print status" heading).
  - **Views:** the switch top right shows a **List**, **Medium pictures** or **Large pictures**. Ctrl+1, 2, 3 or Ctrl+mouse wheel switch too. The view is remembered. Medium pictures show the customer's name instead of time and grams. Large pictures show it before the note. **For**, **Customer** and **Note** are edited in the list view.
  - **Select several:** Ctrl+click, Shift+click or Ctrl+A. **Delete** (or the Delete key) deletes the selected quotes. In the right-click menu, **For**, **Customer name…** and the print status change all selected quotes. Marking several as failed asks first.
  - **Clear all…** deletes every quote, also the ones the filters hide. It first saves a safety copy with all pictures: `%APPDATA%\PrintPrice\history.before-clear-<date>-<time>.printprice-backup`. Every copy is kept. Later, **Backup…** › **Restore** › **Browse…** opens at the newest copy.
  - Delete and Clear all ask first. Afterwards a line says what was deleted (for example "✓ 3 quotes deleted."), with **Undo**. Undo brings back the last delete while the window is open (a new delete or **Backup…** ends it). After Clear all, **Open folder** shows the safety copy.
  - Deleting a failed print that isn't earned back yet stops the payback for it. Deleting a quote that paid back part of a failed print makes that amount owed again.
  - **Quote…** opens the quote page of a saved quote with its saved prices. It shows the same rows as when it was made: the machine wear split, nozzle, build plate, name on the quote and valid days. Quotes saved by older versions show one wear line and today's settings.
  - **Invoice…** makes a bill: the title is Invoice, with a random invoice code (the year and 5 digits, for example 2026-48213), the invoice date and a due date. Older invoices keep their codes. A customer name is required ("Bill to"). A **Payment** block with MobilePay and/or bank details shows only if you set them in Settings. A new invoice starts at **Printed**. The code shows in the **Invoice** column. The due date and payment details are kept with the invoice.
  - A made invoice keeps the name it was made out to: **Invoice…** on it shows that name, read-only, even if **Customer** was changed later. Its price is locked too: the price choice is greyed out ("Price on the invoice (locked)") and **For** can't be changed on invoiced rows.
  - **Several prints on one bill:** see [One bill for several prints](#one-bill-for-several-prints) below.
  - **Invoices…** (Ctrl+I) opens the Invoices window with one row per invoice (a combined invoice is one row): number, customer, prints, invoice date, how long ago, due date (amber once passed and still unpaid), total, print status and **Paid**.
    - **Paid:** click the Paid cell (or press Space) to mark an invoice paid with today's date, or unpaid again. A combined invoice is paid when all its jobs are. A paid invoice's page, PDF and **Copy text** say "Paid 5 November 2026 – thank you" instead of "Please pay by …" and hide the payment details (MobilePay, bank, "Please write invoice no. …"). The page shows no due date.
    - Filter with search, **Status** (also Mixed), **Period** (all time, this month, last 30 days, this year) and **Payment** (All, Unpaid, Overdue, Paid). A count and sum show for what is listed, and a line like "Unpaid: 4 invoices · 138,65 kr. · 1 overdue" (or "Everything is paid."). Ticking Paid or Unpaid shows an **Undo** link that puts it back. The search updates shortly after you stop typing, **Esc** clears it first, and "No invoices match the search or filters." shows when nothing fits. The window fits small laptop screens.
    - Double-click or Enter opens the invoice. Click **Status** to set all its prints at once. **Show in History** selects its prints.
  - **Repeat print** prices the same print again with today's filament and electricity prices and slices the saved link or file again. If that isn't possible, the saved time and weight are used, with a note. If the job no longer fits the chosen printer, the repeat is refused ("not possible on this printer") and can't be saved. The info box shows "Was … → now …". A repeat is never merged with its original. A job with several files slices each file again with its saved filament and colour, and together on one plate if it was before. The plates link comes back too.
  - **Open again** (or double-click, or Enter) puts the inputs back, including the filament name and colour, and recalculates with today's settings. The model box shows the saved price next to today's. Open again and **Repeat print** switch back to the quote's printer, if you still have it.
  - **Picture:** each saved job keeps a small picture (256 px) of the model in `%APPDATA%\PrintPrice\pictures`. The list shows it (point at it for a bigger one), and so do **Open again**, **Repeat print** and a saved job's **Quote…** / **Invoice…**. A job without a picture shows a cube. A deleted job's picture goes once the delete can't be undone any more.
  - All actions are also in the right-click menu.
  - The same quote saved again within 10 minutes replaces the last one. The newest 5000 quotes are kept.
  - Saved quote files are named `<date> <model> - <name>` (several prints: `<date> 3 prints - <name>`). Saved invoices (PDF and picture) are named after the invoice number and the name, for example `2026-48213 - Mette Hansen.pdf`.
  - **Invoice copies:** every invoice PDF you save is also copied into `%APPDATA%\PrintPrice\Invoices`. A copy is never overwritten: a different file with the same name gets " (2)". If the copy fails, the save still works and the message says so. Switch it off with **Keep a copy of every invoice PDF** in **Settings → Quote & invoice**. **Open folder** next to it opens the folder.
  - **Backup…** opens the Backup window (also in Settings > App as **Back up…** and **Restore…**). See Backup and restore below.

<p align="center"><img src="assets/screenshot-history.png" width="480" alt="Screenshot of the History window"></p>
<p align="center"><sub>Model pictures: Printables models by their authors, under their own licences (not the app's). See <a href="assets/history-samples/credits.txt">credits</a>.</sub></p>

<p align="center"><img src="assets/screenshot-invoices.png" width="480" alt="Screenshot of the Invoices window"></p>

<a id="one-bill-for-several-prints"></a>
**One bill for several prints:** in History, select several quotes (Ctrl+click or Shift+click) and click **Quote…** or **Invoice…** (or use the right-click menu). They go on one quote or invoice. The website does this too.
- The prints are shown side by side, up to 3 per row (4 prints are always 4 cards in a row), each with its picture, print time, weight and price. Then comes one price breakdown for the whole bill, and the total.
- **Bill to** and **Prints** are at the top. The printer and plate, when all the prints share them, sit beside the **Price breakdown** heading.
- The print status box covers all the prints ("Mixed" when they differ).
- The minimum price counts once for the whole bill. If the prints together cost less, the difference is shared over the prints' prices and is part of "Work & handling". The saved prices of the prints then add up to the bill's total.
- An invoice gets one invoice number for all the prints, and ticks **Printed** on all of them. **Invoice…** on one print of it opens the whole invoice.
- Failed prints are left off, with a line that says so. Prints already on another invoice are refused, with a line that says which invoice.
- A long bill goes onto several A4 pages ("Page 1 of 2"). Later pages say whose bill they are. The breakdown, total and payment stay together on the last page. When they don't fit under the prints, a line says "Breakdown and total: next page →". Bills of 3–4 prints fit one A4 page.

- **Backup and restore:** the Backup window has two tabs.
  - **Back up** saves the price history and your settings to a `.printprice-backup` file. The file is not locked with a code, and it holds customer names and payment details, so keep it somewhere private.
  - **Restore** reads a backup file or a `history.json`. **Merge** adds the backup's quotes to the ones on this PC. **Replace** swaps this PC's history for the backup's, and asks first. **Also restore the settings** is possible when the file has settings; the Bambu Studio path stays as it is. **Merge** with an older backup also brings back quotes deleted since.
  - File backups include all the pictures of the saved jobs. Online backups take only the newest pictures that fit the service's size limit. A backup from the website restores its pictures too.
  - A safety copy of the current history and settings is saved first, next to them (`history.before-restore-<date>-<time>.json`).
  - **Online, with a code** sends the encrypted history and settings to the backup service and shows a 20-character code (**Copy**). Keep the code safe: it is the only way to get the backup back, it can't be reset, and anyone who has it can read the backup. This PC remembers the code (**Show code**, **New code…**, **Delete online backup…**) and shows "Last online backup: … · kept until …". A backup is kept 400 days after the last upload. The newest upload replaces the older one, also when it comes from another PC with the same code.
  - **Restore from an online code:** choose **An online code**, type the code and click **Get backup**. **Keep backing up to this code** makes later online backups go to that code.
  - **5 online backups a day.** With 1 or 2 left, a note says so. When the limit is reached, the button counts down to the next one ("Next online backup in 3:20:15").
- **Windows:** every window can be dragged by any empty spot. All text (labels, info boxes, prices, status line) can be marked with the mouse and copied with Ctrl+C or right-click Copy. Double-click marks a word.
- **If something goes wrong:** an unexpected error shows the app's own dark "Something went wrong" window instead of Windows' crash dialog, and the app stays open. **Continue** goes on, **Copy details** copies the error (paste it into an issue; there is a link to report it on GitHub), and **Close PrintPrice** closes the app. Each error is also written to `errors.log` in `%APPDATA%\PrintPrice`, without your user name or profile path.
- **Auto-update:** at startup, and then every 10 minutes while the program is open, the app checks this repo's latest release. If there is a newer version, a red **Update to vX** button appears under the Friends & family price. Click it to see what's new in every version since yours (it scrolls if long), then choose **Update now** or **Later**. The download is verified against GitHub's SHA-256 checksum before it's installed. You can switch the check off in Settings (**Check for new versions automatically**); that switches off the 10-minute check too. Opening Settings also checks once (if the automatic check is on). Next to the version number in Settings, **check now** looks once when you click it. A newer version shows there and as the red update button.

## How prices are calculated
```
Material   = grams / 1000 × filament DKK/kg
Power      = hours × printer kW × electricity DKK/kWh
Base       = (Material + Power) × (1 + failed prints %)
Friends    = Base × (1 + friends markup) + hours × machine-time rate
Customers  = Base × (1 + customer markup) + hours × machine-time rate   (at least the minimum price)
```
The machine-time rate is added to both prices and is not marked up. The minimum price is for customers only. Both prices can optionally be rounded up to the nearest N kr. Failed prints are 0 % by default and do not affect the machine-time rate. The failed prints payback (on by default) is added to the price after the minimum price and before rounding.

**Machine-time rate** (kr per print hour, for the printer in use):

| Part | How it's calculated |
|---|---|
| Printer | printer price ÷ 3.000 h (see the table below) |
| Parts and upkeep (PTFE tube, wiper, cutter, belts, extruder gears, rods/bearings, grease, IPA, glue stick, shipping) | parts price ÷ 1.500 h |
| Hotend | 139 kr. stainless (500 h; ~100 h with abrasive filament) or 185 kr. hardened steel (750 h; 300 h abrasive) |
| Build plate | price ÷ life; PETG/TPU halve the smooth PEI plate's life |

Plate prices and lives: Smooth PEI 200 kr. / 600 h, Textured PEI 150 kr. / 1.000 h, SuperTack 260 kr. / 500 h, Cool 150 kr. / 500 h, Engineering 180 kr. / 800 h.

| Printer | Printer price | Parts price | Plates | Default setup |
|---|---|---|---|---|
| A1 | 2.099 kr. | 600 kr. | full price | 1,71 kr/h |
| A1 mini | 1.599 kr. | 500 kr. | 70 % of the price | 1,38 kr/h |
| P1S | 4.199 kr. | 800 kr. | full price | 2,54 kr/h |

The default setup is a 0.4 stainless nozzle, PLA and the smooth PEI plate. All of these numbers are estimates and live in `src/Pricing.cs`.

## Defaults
| Setting | Default |
|---|---|
| Printer | One Bambu Lab A1 |
| Printer power | A1 100 W, A1 mini 70 W, P1S 130 W (average while printing) |
| Wear rate | A1 1,71 kr/h, A1 mini 1,38 kr/h, P1S 2,54 kr/h (worked out from the printer) |
| Markups | 25 % friends & family, 75 % customers |
| Failed prints | 0 % (off; % of material + power) |
| Minimum price | 50 kr. (customers only) |
| Failed prints payback | On; 10 % per quote; paid by customers only |
| Quote | No name on a new install (set it in the setup or Settings), valid 14 days, customer price, dark picture |
| Invoice | Due after 14 days; MobilePay and bank empty; keep a copy of every invoice PDF on |
| History | List view |
| Language | Automatic (Danish if Windows is Danish, otherwise English) |
| Slicing | 0.4 mm nozzle, 0.16mm Optimal, Bambu PLA Matte, Smooth PEI plate |
| Prime tower | On (2+ colours) |
| Prime tower grams on quotes and invoices | Off |
| Electricity | DK1, 0,55 kr/kWh fees on top of spot, 25 % VAT |

Settings, including your printers and the remembered kr/kg price per filament, are saved in `%APPDATA%\PrintPrice\settings.ini`.

When you update from an older version, the old untouched defaults move to the new ones once (minimum price 50 kr., earn back failed prints on at 10 %). Values you changed are kept. Your printer settings become printer 1, unchanged. **Reset to defaults** in Settings asks first, then puts every tab back to the new defaults. It keeps your name, payment details, Bambu Studio path and language, and never touches your history. Nothing is saved until you press OK. On the Printer tab it sets the printer shown to its model's defaults and keeps its name.

Number boxes refuse values of 10.000.000 or more. A `settings.ini` that can't be read is never overwritten: an empty or broken one is kept as `settings.bad-<date>-<time>.ini` and the defaults are used.

## Privacy
The app sends no personal information anywhere. It has no account, cookies or tracking. It only connects to these services:

| Service | When | What is sent |
|---|---|---|
| GitHub | At startup, every 10 minutes while the program is open, and when you open Settings, to look for a new version (all of it can be switched off in Settings); when you click **Update now** | A normal web request with the app name and version. Unchanged answers are not downloaded again (ETag), and if GitHub says "too many requests" the app quietly waits longer |
| Energi Data Service | At startup to get the electricity price (can be switched off in Settings), when you click the price button, or in the setup window's electricity step while the live price is chosen (the default) | The price area (DK1/DK2) |
| PrintPrice's backup service (Cloudflare relay) | Only when you use **Online, with a code** in the Backup window (in the app or on the website): back up, get a backup, new code or delete | The encrypted backup and a storage id made from the code. The backup is locked on your PC (or in your browser) first, and the code itself is never sent |
| elprisenligenu.dk (via the relay, website only) | Only when Energi Data Service doesn't answer the relay's live price request | The relay asks for the day's prices for the price area. Nothing about you is sent on |
| Your browser | Only when you click **GitHub** in "Made by Gettic · GitHub" at the bottom of the main window, the issues link in the message about an unsupported printer, **Open the page** in the MakerWorld message, or **Download Bambu Studio** in the setup window | Opens this project's page (github.com/gettic/PrintPrice), its issues page, the MakerWorld page you pasted or the Bambu Studio download page. Nothing is sent before the click |
| Printables | Only when you paste a Printables link | The model's ID, then the file and a small model picture (~10 KB, from media.printables.com) download |

To enforce the 5-a-day online backup limit, the relay keeps only a salted hash of your IP address (not the address itself) for 2 days.

The website sends nothing about you either. Your settings, price history and files stay in your browser. Online backup sends only a locked copy, and only when you use it. The live electricity price is asked from the same relay and only the price area is sent. The relay takes it from Energi Data Service, or from elprisenligenu.dk (`www.elprisenligenu.dk`) when the first one doesn't answer. The "Prices: elprisenligenu.dk" credit link on the site goes there only when you click it.

Like any web request, these show your IP address to the service. Local `.3mf` files and sliced results use the picture stored inside the file, with no network. Bambu Studio runs on your own PC; the app only starts its command-line slicer, and reads its printer and filament profiles (for slicing and the material drop-down) and the printer pictures from its install folder. This stays on your PC and is never sent anywhere.

Saved quotes (price history) are kept only on this PC in `%APPDATA%\PrintPrice\history.json` (with each invoice's paid date, if you tick it paid) and are never sent anywhere. Their small pictures are in `%APPDATA%\PrintPrice\pictures`, also only on this PC unless you put them in a backup. On the website they stay in the browser. Errors are written to `errors.log` in the same folder and are never sent; **Copy details** puts them on the clipboard only when you click it. **Clear all…** in History clears it, after a safety copy. If the file is damaged, the app keeps it as `history.bad-<date>-<time>.json` in the same folder and starts with an empty history.

While slicing, the app writes temporary files to `%TEMP%\PrintPrice`: copies of Bambu Studio's profiles, a scaled copy of your model if you used Scale, and the sliced result. They stay on this PC and are deleted when the job ends, or at the next start if the app was closed in the middle. A sliced file kept for **Open in Bambu Studio** stays in `%TEMP%\PrintPrice\studio` until the next start.

A backup file holds your price history (customer names), the jobs' pictures and your settings (including MobilePay and bank details). A backup file is written only where you choose to save it and is not locked with a code, so keep it private. An online backup is locked with its code on your PC before it is sent, and nothing is sent unless you click an online button. A restore also leaves safety copies (`history.before-restore-*.json`, `settings.before-restore-*.ini`) in `%APPDATA%\PrintPrice`, and **Clear all…** leaves `history.before-clear-*.printprice-backup` there, with customer names and pictures. Clear all copies are kept until you delete them.

**Open in Studio** starts the Bambu Studio program on your PC with a copy of the sliced file. The copy is kept in `%TEMP%\PrintPrice\studio\` (older copies are deleted) and is never sent anywhere. Bambu Studio may add it to its own list of recent projects.

The **Open folder** link in the Backup window and the **Open folder** button in Settings → Quote & invoice open Windows Explorer at the saved backup file or safety copy, or at the `%APPDATA%\PrintPrice\Invoices` folder, only when you click them. Nothing is sent.

Quotes and invoices are made on your PC. A quote picture or PDF is written only where you choose to save it, and **Copy picture** and **Copy text** put it on the clipboard. A saved invoice PDF is also copied into `%APPDATA%\PrintPrice\Invoices` (unless you switch that off). Nothing is uploaded. The invoice codes already used are kept in `settings.ini`.

Nothing is installed. The app writes only its settings file, the price history, the files you save, the invoice copies and temporary slicing files. To uninstall, delete PrintPrice.exe and the folder %APPDATA%\PrintPrice.

## Build from source
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```
This runs the tests and compiles `bin\PrintPrice.exe` with the C# compiler that ships with Windows, so no SDK or Visual Studio is needed. To regenerate the logo and icon, run `tools\make-icon.ps1`. The switch on its first line picks the AI logo (`'ai'`, made from `assets\logo-master.png`) or the old drawn one (`'drawn'`); line 2 sets the outline colour.

To redraw the README pictures, run `PrintPrice.exe --screenshots <folder>`. This hidden option saves the windows as PNGs in that folder and exits. It uses default settings, never goes online and never saves anything. Add `--lang da` for Danish pictures. A `model.png` in the folder is used as the sample model picture for `main.png`, and a `model2.png` for the multi-colour `main_multi.png`.

For testing, `PrintPrice.exe --settings "<folder>" [--hidden | --minimized]` runs the app on a separate settings folder. The folder is created if missing and starts with defaults. The real `%APPDATA%\PrintPrice` is never touched. There are no update checks or installs, the live electricity price is never fetched (a fixed test price marked "test" is used instead), and the setup window doesn't open. `--hidden` keeps the window off-screen.

### Project layout
| Path | What |
|---|---|
| `src/Pricing.cs` | Price formula and wear model per printer |
| `src/Slicer.cs` | Bambu Studio command-line slicing; merges profile inheritance, which the CLI doesn't do |
| `src/SliceInfo.cs` | Reading sliced files (time, filaments, printer); shared with the website |
| `src/Printables.cs` | Printables API: model files and downloads |
| `src/PowerPrice.cs` | Energi Data Service spot prices |
| `src/Updater.cs` | Update check and install from GitHub releases |
| `src/History.cs` | Saved quotes (price history) |
| `src/HistoryForm.cs`, `src/HistoryViews.cs`, `src/HistoryCustomer.cs`, `src/HistoryInvoices.cs` | The History window; its pictures, List / Medium / Large views, Customer column and Invoices window |
| `src/ScaleForm.cs` | The Scale model window |
| `src/Backup.cs`, `src/BackupForm.cs` | Backup codes and the Backup window |
| `src/OnlineBackup.cs` | Online backup: encrypt, upload, download, delete |
| `worker/` | The Cloudflare relay: online backup and the website's live electricity price |
| `src/PrinterPickerForm.cs` | The Choose printer window |
| `src/QuotePage.cs`, `src/QuoteForm.cs` | The quote and invoice page (picture, PDF) and its window |
| `src/MainForm.cs`, `src/SettingsForm.cs`, `src/UpdateForm.cs`, `src/Theme.cs` | User interface |
| `src/SetupForm.cs` | The first-time setup window |
| `src/FilePickerForm.cs` | The Choose files and colours window |
| `src/ErrorForm.cs` | The "Something went wrong" window and `errors.log` |
| `src/Lang.cs` | English and Danish texts |
| `src/Screenshots.cs` | Hidden `--screenshots` picture mode |
| `tests/PricingTests.cs` | Unit tests |
| `web/` | The website version (reuses the pricing files and the setup's answers in `src/AppSettings.cs`) |
| `.github/workflows/build.yml` | GitHub Actions: builds and tests the release exe |
| `.github/workflows/pages.yml` | GitHub Actions: publishes the website |
| `LICENSE` | GPL-3.0 |

## License
[GPL-3.0](LICENSE). Copyright (C) 2026 Gettic.

**Third-party code:** the website includes Google's Brotli decoder (`web/wwwroot/brotli-decode.min.js`, from [google/brotli](https://github.com/google/brotli) v1.2.1, MIT licence, kept in the file).
