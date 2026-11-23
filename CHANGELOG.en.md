# Release notes

## v1.8.0 — 2026-11-23
- English interface: on first start the app follows the Windows display language (Turkish → Turkish, anything else → English); the choice is saved
- Language picker with flags at the top right (Türkçe / English); switching rebuilds the window in place in the new language (not available while copying or converting)
- Logs, warnings, disc engine messages and release notes follow the selected language; numbers use the matching format (1,8 GB / 1.8 GB)
- The Updates tab is gone: update checks are now a separate section at the top of Settings, and release notes open with the "Release notes" button
- The "New version" chip now opens Settings

## v1.7.0 — 2026-11-23
- The MP4 list is now a table: ISO file · size (MB) · titles/duration · bitrate · estimated MP4 · status
- Separate bitrate for each ISO: click the bitrate cell in the table and type a value; the estimated MP4 size updates instantly
- Automatic bitrate: when an ISO is scanned, the bitrate is calculated so the MP4 comes out at about the size of the ISO, rounded to a multiple of 100 (e.g. 462 MB / 45 min → 1300 kbps); shown as "(auto)" in the table
- The preset bitrate buttons and the new "Auto" button apply to the selected rows (or to all pending rows if nothing is selected)
- The default bitrate in Settings is now only used for ISOs whose duration can't be read

## v1.6.1 — 2026-11-23
- Fix: no video was found in Video CD ISOs. The Windows ISO builder shortens names to 8.3 on the ISO9660 side (AVSEQ01.DAT → AVSEQ010); the real names are now read from Joliet, and every file in the MPEGAV/MPEG2 folder is picked up regardless of extension. Verified end to end with a real VCD
- Simpler MP4 screen: the bitrate value sits next to its label, the encoder description is in a tooltip, deinterlacing is in a section that expands on click, with the audio and resolution note below it; the location row is hidden while "Save next to the ISO" is checked. The log area is noticeably larger
- The system info shows GPU video load as a single value (Intel GPUs encode on the decode engine)

## v1.6.0 — 2026-11-23
- Video CD ISOs can be converted to MP4 too: the MPEGAV\*.DAT files (MPEG2\*.MPG on Super Video CD) inside the ISO are located, a plain MPEG stream is extracted from the raw CD sectors and passed to HandBrake; if there are several videos, the longest becomes the main MP4 and the others separate MP4s
- Settings → MP4 conversion: the default video bitrate and the four preset bitrates can be changed; the MP4 tab starts with the default every time it opens
- When conversion finishes, the number of ISOs that failed is shown as well

## v1.5.1 — 2026-11-23
- System info below the progress bar on the MP4 tab: CPU usage, RAM (used / total), GPU video encode / video decode / 3D usage; GPU temperature on NVIDIA cards and motherboard (ACPI) temperature on computers that support it
- AMD graphics cards are also recognized by HandBrake's new name (vcn)
- When a GPU encoder is selected, decoding the video is also done on the GPU (NVIDIA, Intel)
- Diagnostics: when the tab opens, the graphics cards and available encoders are logged, and during conversion the encoder HandBrake actually uses is logged; if the installed card has no usable encoder, the reason is given

## v1.5.0 — 2026-11-23
- New "Convert to MP4" tab: ISOs are converted to MP4 (H.264 + AAC) with HandBrakeCLI
- Resolution and frame rate stay the same as the source (no cropping/scaling, constant frame rate, DVD aspect ratio preserved)
- Average bitrate setting with presets, plus the estimated output size
- Multi-pass encoding and turbo analysis are off
- Encoder choice: CPU (x264) or, if available, an NVIDIA / Intel / AMD graphics card
- Deinterlacing (Decomb) on/off, with explanations
- Batch conversion: several ISOs or all ISOs in a folder, one after another; drag and drop
- If a DVD has several video titles, the main title plus the others are saved as separate MP4s; "play all" and duplicate titles are skipped
- Settings: "Also convert the ISO to MP4" — conversion starts automatically when copying finishes
- On first use, HandBrakeCLI is downloaded (with your permission) from its official GitHub page and saved under tools\ in the program's folder

## v1.4.2 — 2026-11-23
- When copying starts, the source/destination area collapses to a one-line summary; the log area is much larger
- The status area stays in a fixed place with a fixed height while copying: "Reading normally" when all is well, a yellow warning when there is a problem; the screen no longer jumps around
- Warnings stay on screen for at least 5 s (no flickering); the "read speed dropped" warning only appears if the speed stays low for 5 s
- A log line is written whenever a warning starts and when reading returns to normal

## v1.4.1 — 2026-11-23
- Settings and logs are now saved in the program's folder (MetroKafeDVD2ISO.settings.json, logs\); if the folder isn't writable, %AppData% is used. Old settings are migrated once
- The file-based method works in two steps: files are first copied to a temporary folder (progress, speed and stall warnings work normally; unreadable parts are skipped and logged), then the ISO is built from the local copy in seconds
- The free space check for the file-based method also accounts for the space needed by the temporary copy

## v1.4.0 — 2026-11-23
- Video CD / Super Video CD support: since the video sectors (Mode 2 Form 2) don't fit into ISO sectors one-to-one, the ISO for these discs is built from the files on the disc (like the old DVD2ISO); no more "damaged area" errors
- "Copy method" added to Settings: Auto / Sector by sector / File-based
- The drive info shows the disc type as "Video CD"
- A second data track in the same session is no longer mistaken for a "multisession disc"
- Anchor sectors at the end of UDF discs are calculated correctly for every layout (prevents some discs from being copied short at the end)

## v1.3.1 — 2026-11-23
- The disc's track/session layout (TOC) is read; only data tracks are copied
- On multisession discs, the completely unreadable gaps between sessions are zero-filled without reading and are not counted as damaged; files added in later sessions are visible when the ISO is opened
- On mixed-mode (data + audio) discs, audio tracks are no longer read
- The disc layout is written to the log; each copy's log is saved under %AppData%\MetroKafeDVD2ISO\logs
- "Copy log" button

## v1.3.0 — 2026-11-23
- The disc size is now read from the file system (ISO9660 / UDF); data-less sectors at the end of recordable discs are no longer counted as "bad"
- Extra sectors reported by the drive are tried at the very end: readable ones are added to the ISO, unreadable ones don't count as errors
- The window never touches the disc any more: detection, copying and ejecting run in a separate process, so the window doesn't freeze even if the drive hangs
- A drive that doesn't respond is shown in the list as "drive not responding" after 15 s
- The close button exits the program even if the drive hangs during copying

## v1.2.0 — 2026-11-22
- New Settings tab; options are saved permanently
- Skips a stuck area after a set time (5–60 s); no more hanging forever
- Copies the healthy parts first: damaged areas are skipped quickly and retried at the end
- Lowers the drive speed in damaged areas (on drives that support it)
- Verify when done: the disc is read a second time and compared with the ISO; the SHA-256 is computed and written to a .sha256 file
- Option to eject the disc when done
- Automatic updates: checks for a new version at startup, one-click update

## v1.1.0 — 2026-11-22
- New modern dark interface; custom title bar and logo
- Save location and file name are chosen separately; the default location is the Desktop
- File name presets: Disc label, Disc label + date, Disc label + date + time, Date + time
- Current and average copy speed (MB/s and DVD/CD speed multiplier)
- Scratched/damaged disc warning: shown when reading stalls or slows down
- The Cancel button works even when the drive is stuck
- Updates page and update check

## v1.0.0 — 2026-11-22
- First release: create ISO images from DVDs/CDs
- Skip unreadable sectors and continue
- Free space and FAT32 4 GB limit checks
