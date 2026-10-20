# miniCOCKPIT exploration

## Workspace and scope

- Project root: `C:\gitrepos\github\crgarcia12\mini-cockpit`.
- Source, build scripts, and standalone Windows app: `src`. From the project root, build with `.\src\build.ps1` and launch with `.\src\MiniCockpitLab.exe --connect`.
- Run GUI regression tests with `.\src\test-grid.ps1`; use `-SweepOnly` for the headless PLAY ALL timing/toggle tests.
- Findings live in the project-root `minicockpit.md`. Input and output mappings are `src\mappings.json` and `src\light-mappings.json`; captures remain in `src\captures`.
- The complete experiment was moved from `C:\tmp\minicockpit-lab` into `src`, with every moved lab file hash-verified. Historical mapping snapshots are in `backups`.
- Generated executables, captures, runtime status, test artifacts, backup copies, and local proprietary DLS inspection artifacts are excluded from Git. Source, findings, and the primary mapping files remain trackable.
- No changes to MSFS Copilot, no new drivers, and no firmware writes. Simulator commands are now available only through the opt-in Caravan profile described below.
- DLS was stopped with the user's permission. Close/disconnect the lab before restarting DLS.
- DLS executable: `C:\Users\Carlos\AppData\Local\Programs\miniCOCKPIT Data Link Software\miniCOCKPIT_DLS.exe`.

## Sources and versions

- Public baseline: <https://github.com/janmalec/miniCockpit-serial>.
- Protocol: <https://github.com/janmalec/miniCockpit-serial/blob/master/protocol_minifcu.md>.
- Reference example: <https://github.com/janmalec/miniCockpit-serial/blob/master/talk.py>.
- Baseline latest commit checked: `920b62cec507253682cf0990832b6dec814cf13d`, dated 2024-06-10.
- Installed DLS version: `3.0.19`.
- Installed firmware package filename: `miniFCU_20251113.ino.hex`. This identifies the installed update package, not a firmware version queried from the connected hardware.
- The lab is an independent implementation informed by the published protocol notes; it does not embed the reference Python application.

## Verified on this machine

- DLS's existing log says `Connected: COM5`.
- Windows device: `USB-SERIAL CH340 (COM5)`, USB VID `1A86`, PID `7523`. This identifies the USB serial adapter, not a unique miniCOCKPIT model.
- The existing Windows serial driver is sufficient to open the device; no capture driver is needed for direct access.
- The first open immediately after stopping DLS returned access denied. A later retry opened successfully; allow time for DLS to release the handle.
- Successful direct connection: COM5, 9600 baud, 8 data bits, no parity, one stop bit, no handshaking, DTR false, RTS false.
- Nothing arrived during a three-second pre-initialization read in that probe.
- Sending ASCII `C;`, then `C`, then `C`, produced semicolon-delimited input.
- With no deliberate control movement, the device repeatedly emitted `901;956;958;` at a high rate.
- In the GUI capture, those repetitions stopped after approximately two seconds. Silence afterward did not mean the connection was dead: a later knob movement still arrived, and a display command worked.
- These codes are absent from the 2024 baseline. Later DLS inspection identified them as controller/EFIS/overhead connection status; see the BARO diagnosis below. Do not strip a leading 9 or label them as button events.
- User confirmed three panels: miniFCU, miniEFIS, and mini-overhead.
- Only one non-Bluetooth serial port was identified. FCU, EFIS, AND mini-overhead inputs have now all been received on COM5.

## Baseline claims still to verify on this hardware

The old notes describe numeric input codes for FCU knobs/buttons and these outgoing display commands:

| ASCII command | Claimed effect |
|---|---|
| `C` | Start receiving data |
| `A5000` | Display altitude 5000 |
| `H43` | Display heading 43 |
| `S105` | Display speed 105 |
| `V600` | Display vertical speed 600 |

Display framing, negative values, lights, mode labels, brightness, current miniEFIS controls, and additional devices are not established.

## Confirmed interactive trials

Capture: `captures\20261018-215742-237.jsonl`.

| Trial | Result | Confidence |
|---|---|---|
| Send exact ASCII `H234`, no terminator | User confirmed physical FCU heading display showed `234` | Confirmed by user |
| Turn FCU heading knob clockwise exactly one detent | Received exactly `3;` (`33 3B` hex), once | One controlled trial; repeat to establish reliability |
| Send exact ASCII `S123`, no terminator | User confirmed speed `123` | Confirmed by user |
| Send exact ASCII `A5678`, no terminator | User reported altitude `05600`, NOT `05678` | Confirmed discrepancy; likely 100-ft display granularity, not yet established |
| Send exact ASCII `V900`, no terminator | User confirmed V/S `+900` | Confirmed by user |
| Turn heading counterclockwise one detent | `4;`, once | Ordered user trial |
| Push heading knob and release | `1;`, once | Ordered user trial |
| Pull heading knob and release | `2;`, once | Ordered user trial |
| Press EFIS FD once | `62;`, once, on COM5 | Ordered user trial; new mapping absent from baseline |
| Send `A12300` | User confirmed `12300` | Confirms an exact 100-ft-aligned value works |
| Send `V-700` | User confirmed `-700` | Negative vertical-speed display supported |
| Overhead APU MASTER | `206;` | Corrected by user's full mapping sweep |
| Overhead APU START press | `207;` | Corrected by user's full mapping sweep |
| Overhead APU START release | `577;` | Corrected by user's full mapping sweep; stored label is `ap.start.release` |
| Overhead SEAT BELTS OFF to ON | `211;`, once | Ordered user trial; reverse position not tested |

The display command and input were observed with DLS stopped and without any simulator integration.
The initial ordered trial incorrectly attributed `206` to BARO rotation and `207` to APU MASTER. The later full mapping sweep supersedes that inference. Separate APU START press/release events explain why counting packets against physical actions was unreliable.

## DLS inspection and BARO diagnosis

- All 83 nonempty descriptions were saved when checked on 2026-10-19. Independent backup: `backups\minicockpit-mappings-20261019-152752.json`; SHA-256 matched the live mappings file.
- Before restarting, the user added `101 = qnh.clockwise`, `102 = qnh.counterclockwise`, and renamed `70` from `Pressure.push` to `qnh.push`. All 85 descriptions at that point were backed up to `backups\minicockpit-mappings-20261019-161344.json`; SHA-256 matched before and after restart.
- User permits restarting the lab provided mappings are preserved.
- User reported BARO display `88.88`, with QFE and QNH both lit. Clockwise/counterclockwise rotation changed neither the display nor the raw capture after the diagnostic marker.
- Installed DLS is mixed native/managed C++ code. Existing `ilspycmd` can inspect the managed transport and simulator handlers locally. Inspection artifacts are in `src\dls-inspection` (Git-ignored); no DLS code is incorporated into the lab.
- `SerialMain.SERIAL_init` and `RETRIEVE_PROTOCOL` identify `901` as controller connected, `956` as EFIS connected, and `958` as overhead connected. `957`/`959` clear the corresponding EFIS/overhead connection flag. These are not unmapped physical controls.
- DLS's output table and `SERIAL_send_v` establish numeric framing as prefix + decimal integer + comma. Its original table is at executable file offset `0x2699c0` (specific to this installed binary).
- `MSFS.send_EFIS` uses `_` for pressure in hundredths of inHg, `#` for whole hPa, `@` for the pressure mode, and `{` for numeric/STD display state.
- Examples recovered from the DLS code: `_2992,` = 29.92 inHg; `#1013,` = 1013 hPa; `@1,` = QNH mode; `{1,` = non-STD numeric display (`{0,` is used for STD). The inHg and STD effects have not been physically tested.
- DLS treats inputs `101` and `102` as BARO rotary activity and temporarily suppresses pressure readback while turning.
- Sent `{1,`, `@1,`, and `#1013,` through the running lab. User confirmed `1013`, QNH only, and working rotation. Raw capture then showed `101,1014;` through `101,1017;`, followed by `102,1016;` down to `102,1012;`: 101 increases and 102 decreases the current pressure payload, in hPa for this trial.
- The verified sequence is now part of the lab's panel initialization: it starts the standalone EFIS in numeric QNH at 1013 hPa. This is a lab default, not a simulator pressure readback. No continuous pressure writes are made, so the user's rotary changes remain visible.
- Inspection references: `SerialMain.decompiled.cs` transport/connection handling; `Module.decompiled.cs` `MSFS.send_EFIS`; `StockA320.decompiled.cs` pressure conversion into EFIS output slots.

## Fast self-service UI

### Start and Test

- The app opens on **Start**: a saved aircraft preference, a connect/disconnect button, and live connection cards for miniFCU, miniEFIS, and miniOVERHEAD. Choose an aircraft from the dropdown; the choice is saved locally in `src\aircraft.txt`.
- Aircraft selection defaults to **Auto-detect (MSFS)**, which attempts a read-only SimConnect connection on startup and resolves the profile from the simulator's aircraft title. Recognized families are C208/Caravan, A320/A32NX, A321, and A330-900/A339. Unknown aircraft remain explicitly unrecognized, never a fallback Caravan. Choose a specific aircraft for a persistent manual override; select **Auto-detect (MSFS)** to follow the simulator again. Existing saved manual choices remain respected. **Connect MSFS** is available for every profile; only the Caravan currently supports simulator commands.
- Green lights require the actual hardware reports: `901` for the FCU/controller, `956` for EFIS, and `958` for overhead. An open serial port alone shows waiting, not connected panels. `957`/`959` clear EFIS/overhead status; disconnecting or losing the serial connection clears all three. Quiet connected panels do not time out.
- **Test** contains the complete existing workspace: serial settings, display commands, input mappings, lights/screens, captures, logs, and batch commands. Set a different COM port or baud rate there before connecting if necessary.
- Inputs received on Start are still captured but never focus the hidden mapping editor. Returning to Start saves the active mapping, stops/restores any active output test, cancels queued batch commands, and disarms learning.
- **Quick test** on Start lights all 29 physical button annunciators together for three seconds. It automatically switches them off; click **Stop quick test** to end early. It never changes mappings or sends simulator commands. Connected simulator indicators are restored afterward. LCD mode tests remain in the detailed explorer because their mutually exclusive states cannot all be enabled together.
- **Test** opens on **HARDWARE**, with local photographs arranged **miniEFIS left / miniFCU center / miniOVHD right**, matching the connection cards on Start. Green control rectangles disappear two seconds after the last physical input; explicit CALL, APU START, and external-power releases clear the corresponding pulse immediately. Simulator updates never prolong these rectangles; actual annunciators and mode dots remain simulator-driven. Unknown inputs remain visible in the input banner without inventing a position. All original tools are under **MAPPINGS / TOOLS**.

### Caravan / MSFS integration

- Leave **Auto-detect (MSFS)** selected or choose **Cessna 208B Grand Caravan** manually, connect the hardware, then use **Connect MSFS** if not already connected. Manual profile changes do not disconnect aircraft telemetry. The independent x64 SimConnect client does not change MSFS Copilot or use its Airbus-specific FCU endpoints.
- Commands require fresh telemetry identifying a loaded Caravan, the selected Caravan profile, connected/initialized hardware, and a live Start/HARDWARE view. Entering mapping tools pauses simulator control and hardware readback writes; Quick test and other output tests also take exclusive ownership. No simulator commands are sent merely by connecting.
- AP1 (`50`) toggles `AP_MASTER`. AP2 (`51`) toggles NAV mode with `AP_NAV1_HOLD`, independently of FLC and exterior navigation lights. Both HDG push/pull (`1`/`2`) send `AP_PANEL_HEADING_ON`, enabling heading mode without capturing/replacing the selected heading. Both ALT push/pull (`15`/`16`) send `AP_ALT_HOLD_ON` to hold altitude. Both SPD push/pull (`11`/`12`) send `FLIGHT_LEVEL_CHANGE_ON`. Push/pull are idempotent mode activation, not toggles.
- HDG rotation (`3`/`4`) changes the heading bug; ALT rotation (`17`/`18`) changes the selected altitude; SPD rotation (`13`/`14`) changes selected airspeed while FLC is active. V/S rotation (`21`/`22`) changes the vertical-speed reference. HDG/SPD/V/S numeric payloads set exact validated targets; payload-free frames use the simulator's increment/decrement events. Negative V/S values use the SDK's signed value encoded in the event's uint payload.
- NXi intercepts `AP_ALT_VAR_SET_ENGLISH` as a single knob movement, not an absolute target (confirmed against the running Caravan). ALT therefore uses `AP_ALT_VAR_INC`/`DEC`: one detent is 100 ft, or 1000 ft when the physical payload differs from the current simulator target by exactly 1000 ft. The selected-altitude readback remains authoritative; stale local display values never cause a large absolute jump.
- AP1 and AP2 annunciators follow `AUTOPILOT MASTER` and `AUTOPILOT NAV1 LOCK`. Managed dots indicate HDG, ALT HOLD, and FLC modes. Physical FCU displays and the on-screen photograph show the selected MSFS heading, altitude and vertical speed. SPD shows selected airspeed while FLC is active and dashes otherwise. These are readbacks, never optimistic button-state toggles or fixed sample values.
- Overhead STROBE ON/OFF, BEACON, NAV & LOGO, LAND and NOSE positions set the Caravan's exterior lights with explicit set events, not toggles. STROBE AUTO follows `SIM ON GROUND`; NAV 1 and 2 both mean navigation lights on; LAND RETRACT means off; NOSE TAXI and T.O both use the Caravan taxi light.
- RWY TURN OFF and NOSE share the Caravan taxi light: it remains on while either of their last observed physical positions is on. These observed positions and strobe AUTO ownership reset on disconnect/profile changes and when entering/leaving mapping tools. Unmoved switch positions are not inferred.
- Other overhead systems are still input-test/mapping only. `status.json` includes simulator connection, freshness, loaded aircraft, autopilot/light readbacks, control eligibility, and Quick test state. Diagnostic local commands include `aircraft`, `quick-test`, `sim-connect` (connect/disconnect), and guarded `sim-input`; diagnostic input writes are explicitly marked in the capture and do not pretend to be physical RX.
- Build with `.\src\build.ps1 -SimConnectDirectory <directory>` or set `MINICOCKPIT_SIMCONNECT` to an installed x64 SDK runtime directory containing `Microsoft.FlightSimulator.SimConnect.dll` and `SimConnect.dll`. Existing copies in `src` are reused; on this machine the existing sibling MSFS Copilot SDK runtime is also detected. Runtime DLLs are not committed. GUI tests copy the runtime and image assets into their isolated test directory.

### Hardware image references

The hardware view uses cropped official product photographs, retained locally for offline device identification; miniCOCKPIT owns the photographs and product marks. References consulted on 2026-10-20:

- miniFCU: <https://shop.minicockpit.com/products/minifcu> — `minifcu-front_1.jpg?v=1763446403`.
- miniEFIS: <https://shop.minicockpit.com/products/miniefis> — `miniefis-front.jpg?v=1763446403`.
- miniOVHD: <https://shop.minicockpit.com/products/miniovhd> — `ovhd-shopify-white-1.jpg?v=1764824440`.
- Images are from the product galleries' `cdn.shopify.com/s/files/1/0834/0547/2063/files/` URLs. Hotspots use the photographed control positions, independently of user-editable mapping descriptions.
- Simulator events: <https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/Key_Events/Aircraft_Autopilot_Flight_Assist_Events.htm> and <https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/Key_Events/Aircraft_Electrical_Events.htm>.

### Light mapping

- Open the **LIGHTS / SCREENS** tab, then **START / resume**. The lab switches all 29 known annunciator channels off, then tests the first unnamed output. Existing annunciator mappings are preserved, so a completed lamp sweep resumes at the new LCD tests.
- Look at the physical indicator, type its description (for example `apu.master.on`, `apu.master.fault`, or `apu.start.avail`), and press TAB. The name is saved before the old light switches off and the next switches on; typing focus stays in the name box.
- A blank name skips a channel without erasing an existing name. Click any row to retest that output. STOP saves the current name and sends its off/restore commands; ALL LIGHTS OFF clears the 29 button annunciators and restores the active LCD test without replaying every mutually exclusive screen mode.
- **PLAY ALL (100 ms)** runs in this same explorer: all 54 output tests ON in list order, followed by all OFF/restore sequences in list order, with at least 100 ms between output steps. Multi-command LCD tests stay grouped. Mutually exclusive LCD modes replace each other.
- The PLAY ALL button changes to **STOP** during playback. Clicking that same button cancels playback and restores its PLAY ALL label; completion, disconnect, and playback errors also restore the label.
- PLAY ALL clears the button lights before beginning and never edits mappings. STOP, selecting another output, leaving the tab, or closing cancels the sweep and switches off/restores the outputs reached. Serial errors stop playback and surface through the normal error handler; disconnect pauses playback.
- Leaving the light tab or closing the app saves the active name and switches off its light while connected. Input events continue to be captured but cannot steal naming focus in the light tab.
- Output names are separate from button inputs: `src\light-mappings.json`. Each saved entry includes panel, description, exact ON command, and exact OFF command.
- Each save also atomically updates the project-root `minicockpit-light-mappings.json`, outside `src`. Save failures are surfaced and do not advance to another light.
- The catalogue contains 54 output tests: the original 16 overhead outputs, six FCU annunciators, seven EFIS annunciators, plus 25 LCD/value/mode tests recovered from DLS. Physical names are assigned only after seeing the hardware effect.
- LCD tests cover QNH, QFE, STD, hPa and inHg (including the decimal point); speed, Mach, heading, altitude, V/S, and FPA values; negative V/S/FPA for the minus sign; five dashed-display states; three managed-mode dots; and four LCD control pairs (`I/i`, `Y/y`, `W/w`, `O/o`) to identify on the hardware.
- LCD sample values are fixed lab test values, not simulator data. Each output's saved `OnCommand`/`OffCommand` can contain a newline-separated sequence; the app transmits each line as its own exact command, without transmitting the newline. The grid shows both test and restore sequences plus a DLS-function hint.
- Dot command pairs recovered from `MSFS.send` output slots 9/10/11: `z,`/`x,` for speed, `m,`/`s,` for heading, and `a,`/`q,` for altitude. Dashed-display commands are `d,`, `D,`, `h,`, `v,`, and `f,`, with the same preparation/restore controls used by DLS.
- DLS `SERIAL_send_OVHD(73, address, state)` encodes `\` + decimal address + state + comma, with no separator between address and state. Example: `\161,` turns overhead address 16 on; `\160,` turns it off. The 16 addresses used by DLS are 16, 24, 17, 25, 12, 20, 11, 19, 10, 18, 13, 21, 15, 23, 14, 22.
- FCU ON/OFF command pairs are `P,`/`p,`, `U,`/`u,`, `T,`/`t,`, `L,`/`l,`, `E,`/`e,`, `R,`/`r,`. EFIS prefixes are `5`, `4`, `3`, `2`, `1`, `0`, `!`, followed by `1,` (on) or `0,` (off).
- These writes affect physical indicators only; they do not operate simulator systems.

### Input mapping

- Inline focus-following is ON by default. Touch any control (known or unknown): the main list scrolls to its row and focuses its description cell for typing. No automatic popup.
- Type a description and TAB to save. Moving a different control commits the previous edit and focuses that input's description.
- Repeated events/rotary values for the same control preserve the current typed description and caret position.
- F8 pauses/resumes focus-following. Existing mappings are prefilled and can be renamed.
- Empty descriptions do not erase existing labels. Failed saves keep the edit active and surface an error.
- Startup initialization traffic is captured but excluded from automatic naming during the settling interval.
- Move controls freely: the prominent LAST INPUT banner and chronological input log show incoming tokens and observed labels.
- The grid retains totals and trial counts for each code. Edit Observed mapping directly.
- Fastest mapping: touch ONE control, click NAME LAST INPUT, select/type its panel, control, and action, then SAVE MAPPING. The captured event stays frozen while the naming dialog is open.
- Double-click a row (outside the editable mapping cell), or use Name selected row, to label an earlier input.
- Unknown/unlabeled input rows are highlighted yellow. Existing mappings require confirmation before replacing them in the naming dialog.
- The original pre-armed LEARN NEXT INPUT mode is also available.
- Frames such as `17,12300;` now display code `17` and value `12300` separately. Generic labels apply across changing values. Check Only this exact value in the naming dialog when the value represents a selector position instead of a changing rotary value.
- Learned labels persist to `mappings.json` and append a timestamped observation to this findings file.
- User-saved additions `61 = Chrono`, `64 = CSTR`, and `65 = WPT` were present before the click-free upgrade and were preserved.
- FCU display fields have individual Set buttons.
- Exact ASCII command accepts arbitrary commands with selectable no suffix, semicolon, or CRLF framing.
- COMMAND CONSOLE / BATCH accepts one arbitrary ASCII command per line, transmits the entire list with configurable delay, and provides STOP BATCH.
- Only the displayed FCU examples are verified. A general command console is not a complete list of all firmware commands; unknown output/light commands still need discovery.
- Raw RX is hidden from the live view by default to reduce noise, but full bytes remain in captures.

## Capture workflow

1. Run `MiniCockpitLab.exe --connect`, with DLS closed.
2. The app waits three seconds, then sends the reference initialization sequence.
3. Raw RX/TX bytes and timestamped trial markers are saved to `captures\*.jsonl`.
4. The grid aggregates repeated tokens so idle messages do not hide new events. Unknown tokens are retained.
5. Give each physical action its own MARK: one component, one control, one direction, one detent/press.
6. Observe both the raw code and repeat/release behavior before assigning an observed mapping.
7. Test displays separately using known values. A successful serial write proves transmission, not that the display accepted it; ask the user to verify.
8. `status.json` exposes current connection/counters. A local `command.json` can request `mark`, `send`, `connect`, `disconnect`, `learn`, `map`, `batch`, or `cancel-batch`; the app records success/failure in `response.json`. No network listener is opened.

## Open questions

- Connection codes `901`, `956`, and `958` are resolved by DLS inspection; other undocumented codes remain open.
- Are input codes unique across all controls on the three panels? Known samples are distinct; a complete sweep is still needed.
- What is the precise altitude granularity/formatting rule? `A5678` showed `05600`; `A12300` showed `12300`.
- What framing, initialization, and keepalive behavior does the current firmware require?
- What additional controls and indicators need protocol extensions?
- Caravan AP/FLC/ALT/SPD and exterior lights now have an opt-in SimConnect profile (above); remaining aircraft and systems are not integrated.

## Current lab status

- The relocated self-service window preserves all 88 saved input descriptions and 29 saved light descriptions. The original captures, local DLS inspection, and mapping snapshots were moved intact.
- Restart capture `captures\20261019-141352-395.jsonl` confirms the original three handshake writes followed by `{1,`, `@1,`, and `#1013,`. The app is responsive, COM5 is connected, and no error is reported.
- A four-line batch (`S123`, `H234`, `A12300`, `V-700`) was transmitted in order and logged successfully.
- The monitor, single-command sender, multi-command batch console, and learn-next-input workflow can be used without waiting for chat instructions.
- All three panels have produced inputs on the same existing serial connection; no new driver or DLS process was used.

## User-assigned observations from the lab

### Additional raw observations, 2026-10-19

- Independent user activity produced `17,12300;`, `18,12300;`, `59;`, `60;`, `21;`, and `22;`.
- Action meanings have NOT been assigned from this unmarked activity. The user can name them using the touch-first dialog.
- The comma separates a numeric event identifier from a payload in the observed frames. Raw payloads remain unchanged in capture files; the UI no longer treats each rotary value as a separate unknown control.

- 2026-10-19T08:27:28.6023207Z: token `18,12300;` labeled **** in the lab (grid edit; user-assigned observation).

- 2026-10-19T11:45:49.0805063Z: token `61;` labeled **Name the next control you will move** in the lab (learn next input; user-assigned observation).

- 2026-10-19T11:46:02.6018402Z: token `61;` labeled **Chrono** in the lab (learn next input; user-assigned observation).

- 2026-10-19T11:46:12.3948492Z: token `64;` labeled **CSTR** in the lab (learn next input; user-assigned observation).

- 2026-10-19T11:46:18.9688675Z: token `65;` labeled **WPT** in the lab (learn next input; user-assigned observation).

- 2026-10-19T13:02:28.0126732Z: token `66;` labeled **VOR.D** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:32.2212214Z: token `67;` labeled **NDB** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:38.2259638Z: token `68;` labeled **ARPT** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:41.4815066Z: token `63;` labeled **LS** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:48.2510144Z: token `72;` labeled **ROSE.VOR** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:53.8186090Z: token `73;` labeled **ROSE.NAV** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:02:59.0078343Z: token `71;` labeled **ROSE.LS** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:03.4395784Z: token `74;` labeled **ROSE.ARC** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:15.2696189Z: token `75;` labeled **ROSE.PLAN** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:34.3532462Z: token `80;` labeled **VIS.10** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:37.9264859Z: token `81;` labeled **VIS.20** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:42.3684945Z: token `82;` labeled **VIS.40** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:45.8678284Z: token `83;` labeled **VIS.80** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:49.5024625Z: token `84;` labeled **VIS.160** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:03:54.7516166Z: token `85;` labeled **VIS.320** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:03.7721857Z: token `103;` labeled **inHg** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:07.9737013Z: token `104;` labeled **hPa** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:21.8842319Z: token `70;` labeled **Pressure.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:31.9395677Z: token `69;` labeled **Pressure.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:40.9377059Z: token `56;` labeled **SPDMACH** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:04:56.6038143Z: token `13;` labeled **Speed.right** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:00.9786225Z: token `14;` labeled **Speed.left** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:12.6647744Z: token `3;` labeled **heading.clockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:22.8218135Z: token `4;` labeled **heading.counterclockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:30.1152301Z: token `14;` labeled **speed.counterclockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:44.3815844Z: token `13;` labeled **speed.clockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:46.5131991Z: token `53;` labeled **loc** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:48.3375804Z: token `50;` labeled **ap1** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:50.3471798Z: token `51;` labeled **ap2** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:05:56.1690528Z: token `52;` labeled **a/thr** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:04.8064517Z: token `57;` labeled **hdgtrk** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:10.5065458Z: token `17;` labeled **altitude.clockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:16.8240888Z: token `18;` labeled **altitude.counterclockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:21.2631211Z: token `15;` labeled **altitude.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:26.5232720Z: token `16;` labeled **altitude.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:31.6640109Z: token `1;` labeled **heading.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:36.0606600Z: token `2;` labeled **heading.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:40.6940111Z: token `11;` labeled **speed.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:45.2179251Z: token `12;` labeled **speed.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:06:54.2514447Z: token `19;` labeled **verticalspeed.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:03.4409765Z: token `20;` labeled **verticalspeed.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:07.3091839Z: token `21;` labeled **verticalspeed.clockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:14.3101667Z: token `22;` labeled **verticalspeed.counterclockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:20.7692144Z: token `58;` labeled **metricalt** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:28.1460432Z: token `200;` labeled **ldgflap3** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:32.2614017Z: token `578;` labeled **call** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:46.4146013Z: token `208;` labeled **call.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:07:56.7971838Z: token `578;` labeled **call.release** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:05.4633653Z: token `201;` labeled **wingai** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:10.8607390Z: token `202;` labeled **engai** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:17.8068542Z: token `203;` labeled **winheat** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:23.8184648Z: token `204;` labeled **apubleed** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:36.7603101Z: token `206;` labeled **apu.master** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:08:41.2079556Z: token `204;` labeled **apu.bleed** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:02.3915597Z: token `207;` labeled **ap** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:03.2661450Z: token `577;` labeled **uy** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:16.4411700Z: token `207;` labeled **apu.start.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:23.1518465Z: token `577;` labeled **ap.start.release** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:34.6567273Z: token `575;` labeled **extpwr.release** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:41.0828346Z: token `205;` labeled **extpwr.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:45.4805688Z: token `221;` labeled **dome.brt** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:50.6779147Z: token `222;` labeled **dome.dim** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:09:56.0478220Z: token `220;` labeled **dome.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:01.6153892Z: token `211;` labeled **seatbelt.on** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:15.3020856Z: token `210;` labeled **seatbelt.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:19.8711501Z: token `231;` labeled **wiper.s** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:24.4582583Z: token `232;` labeled **wiper.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:29.2724514Z: token `233;` labeled **wiper.f** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:43.7291468Z: token `224;` labeled **light.rwy.on** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:48.8211417Z: token `223;` labeled **light.** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:10:54.7440915Z: token `223;` labeled **light.rwy.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:04.3424535Z: token `226;` labeled **light.land.on** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:09.3019691Z: token `227;` labeled **light.land.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:16.7969259Z: token `225;` labeled **light.land.retract** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:24.0575776Z: token `229;` labeled **light.taxi.to** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:32.8712335Z: token `230;` labeled **light.taxi.taxi** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:37.9970296Z: token `228;` labeled **light.nose.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:42.6206186Z: token `229;` labeled **light.nose.to** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:49.9353013Z: token `230;` labeled **light.nose.taxi** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:54.6330439Z: token `213;` labeled **light.strobe.on** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:11:58.8220227Z: token `214;` labeled **light.strobe.auto** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:04.5801353Z: token `212;` labeled **light.strobe.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:08.7126365Z: token `215;` labeled **light.beacon.on** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:14.8906994Z: token `216;` labeled **light.beacon.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:19.3894512Z: token `218;` labeled **light.nav.2** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:22.9649390Z: token `219;` labeled **light.nav.1** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:12:30.6815625Z: token `217;` labeled **light.nav.off** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:15:50.5739302Z: token `76;` labeled **VIS.OFF** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T13:16:12.0489062Z: token `76;` labeled **ROSE.OFF** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:12:40.3412393Z: token `101;` labeled **qnh.clockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:12:50.3968257Z: token `102;` labeled **qnh.counterclockwise** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:12:55.8015496Z: token `70;` labeled **qnh.push** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:09.0925042Z: token `69;` labeled **qnh.pull** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:23.7087540Z: token `103;` labeled **qnh.inHg** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:27.5622531Z: token `104;` labeled **qnh.hPa** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:46.2735683Z: token `901;` labeled **connected.controller** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:52.7350804Z: token `956;` labeled **connected.efis** in the lab (inline description edit; user-assigned observation).

- 2026-10-19T14:14:58.0754449Z: token `958;` labeled **connected.ovhd** in the lab (inline description edit; user-assigned observation).
