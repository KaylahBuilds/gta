# On The Blade — site

Static site for ontheblade.com. No build step: open `index.html` or serve the folder.

## Pages
- `index.html` — landing
- `features.html` — systems deep dive (interactive strategy switcher, corner map)
- `install.html` — install guide
- `manual.html` — full manual, all config keys
- `changelog.html` — release history
- `faq.html` — troubleshooting and Patreon questions

## Assets
- `support.js` — runtime the pages load (required, same folder)
- `plate.png`, `plate2.png` — gameplay backdrops, cropped from an in-game screenshot

## Deploying to GitHub Pages
Push this folder's contents to the repo root (or `/docs`), then Settings → Pages → deploy from branch.

## To do
- Set the YouTube video id on the landing page's video block (currently a "footage coming" card)
- Reconcile version: manual says v0.2.0, changelog/landing say v0.1.0
