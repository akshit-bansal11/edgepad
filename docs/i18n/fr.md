# French (fr) glossary

Android `values-fr`, Windows culture `fr`. One French for France and Canada: no regionalisms (no "courriel", "magasiner", "char"), words that Android and Windows use in both.

## Conventions

- Register: "vous", imperative for buttons (infinitive for short action labels such as "Oublier", "Activer"). Never "tu".
- Quotes: « » with a narrow no-break space (U+202F) inside. Narrow no-break space also before `?`, `!`, `;` and `%`. No-break space (U+00A0) before `:` and between a number and its unit (`5 dp`, `12 ms`, `60 Hz`).
- Units stay as in English (`dp`, `Hz`, `ms`, `s`, `MB`), per the term list.
- Titles and labels: sentence case, only the first word capitalised (a screen name such as "Commandes" counts as a name).
- Plurals: `one`, `many`, `other`. `many` (millions) uses the "de" form (`%1$d de secondes`), never reached in practice.
- Apostrophes are straight `'`, escaped `\'` in Android XML.
- Key caps (French print): Ctrl, Alt, Maj (Shift), Fn, Suppr, Entrée, Échap, Verr. maj, Tab, Retour arrière. Windows logo key left as Windows labels it.
- Gendered words avoided in joiners: "Pas de connexion à X" instead of "X n'est pas connecté".
- Joiners: `list_separator` ", ", `sentences_joined` "%1$s %2$s", `corner_slot` "%1$s · %2$s" (edge nouns are all "bord ...").

## Terms

| English | Français |
|---|---|
| Edgepad, Bluetooth, Wi-Fi, USB, Windows, Android, Mac | unchanged |
| laptop | ordinateur (never "portable": in France that means a mobile phone) |
| phone | téléphone |
| surface / control surface | écran principal ("Écran de commande de l'ordinateur" in accessibility descriptions); "Surface settings" = Paramètres de l'écran principal |
| dial / edge ruler / ruler | règle (one word for both); "Corner dials" = Règles d'angle |
| corner, split corners | coin, Coins doubles |
| edge | bord (bord gauche / supérieur / droit / inférieur) |
| volume, brightness, zoom | Volume, Luminosité, Zoom |
| media scrub | Position de lecture |
| app switcher | Changer d'appli |
| refresh rate | Fréquence d'actualisation |
| vertical / horizontal scroll | Défilement vertical / horizontal |
| virtual desktops, task view | Bureaux virtuels, Vue des tâches |
| browser tabs | Onglets du navigateur |
| switch laptop | Changer d'ordinateur |
| Dial short labels | VOL, LUM, ZOOM, LECT, APPLI, FRÉQ, DÉFIL, HORIZ, BUR, ONGL, ORDI |
| trackpad | pavé tactile (Windows' own word) |
| pointer | pointeur |
| gesture | geste |
| natural scrolling | Défilement naturel |
| now playing | Lecture en cours |
| media | média |
| Controls (grid) | Commandes |
| control colour | Couleur des règles |
| keyboard | Clavier |
| function keys, With Fn, Direct | Touches de fonction, Avec Fn, Direct |
| gamepad | Manette |
| gamepad presets | Xbox / Plateforme / Course / Tir; the Xbox keys preset (separate from Xbox) is Xbox (touches). Keys `gamepad_preset_*`; the guide uses the same words. |
| layout | disposition; preset = modèle |
| binding | Assignation |
| D-pad, stick, button, shoulder, trigger | croix directionnelle, joystick, bouton, bouton d'épaule, gâchette |
| keyboard mode | Mode clavier |
| macro | Macro (one word on both apps) |
| annotate / sign | Annoter / Signer |
| capture | Capture |
| shapes | Formes |
| lock the trackpad / pad | Verrouiller le pavé tactile / le pavé |
| focus (hide dials and media) | Masquer les règles et le média |
| share / share clipboard | Partager / Partager le presse-papiers |
| clipboard | presse-papiers |
| pairing, pair | association, associer |
| pairing code | code d'association |
| PIN | code PIN |
| trust / Don't trust | Approuver / Refuser (Windows `Dialog_Trust`, `Dialog_DontTrust`; the guide quotes "Approuver") |
| trusted phone | téléphone approuvé |
| security level | niveau de sécurité |
| forget | Oublier |
| remembered | Enregistré |
| Devices | Appareils |
| active / make active | Actif / Activer |
| connect, disconnect, reconnect | Connecter, Déconnecter, Reconnexion |
| stay connected | Rester connecté en arrière-plan |
| handshake | échange initial |
| link / connection | connexion |
| Nearby devices (permission) | Appareils à proximité (Android's label), quoted in running text: l'autorisation « Appareils à proximité » |
| USB tethering | Partage de connexion par USB (Android's label) |
| haptic feedback | Retour haptique |
| appearance / Look | Apparence / Aspect |
| theme System / Dark / Light | Système / Sombre / Clair |
| upright / sideways | Portrait / Paysage |
| tray (notification area) | zone de notification |
| tray menu | menu d'Edgepad dans la zone de notification |
| Wi-Fi and USB connections | Connexions Wi-Fi et USB |
| firewall | Pare-feu Windows |
| start with Windows | Démarrer avec Windows |
| update / release notes / release page | mise à jour / Notes de version / Page de la version |
| protocol | protocole |
| round trip | Temps de réponse |
| settings | Paramètres (Android and Windows both say so) |
| dial feel | Comportement des règles |

## Windows and shared core

- Windows' own words: "paramètres" (Windows Settings: "paramètres Bluetooth"), "zone de notification", "pare-feu Windows", "journal" (log). Android says the same: "Paramètres", not "Réglages".
- Dialog_Trust / Dialog_DontTrust: Approuver / Refuser. The Android guide and error text quote "Approuver".
- Tray menu: Macros…, Sécurité…, Connexions Wi-Fi et USB (quoted verbatim by the Android guide), Partager le presse-papiers, Documentation.
- Access keys (unique per menu): tray D(Démar**r**er)=R, M, S, W, P, F, B, J, N, O, D, Q; Security S/P/O/R; Macros A/P/C/U/S/E.
- Security levels: "Approbation seule", "Code PIN une fois par téléphone", "Code PIN à chaque connexion". "PIN" alone only in short buttons; sentences say "code PIN".
- Forget all = "Oublier tout…"; Reset = "Réinitialiser"; both quoted verbatim in the Error_* texts.
- Status text avoids agreement with the phone's name ("Téléphone refusé : …").
