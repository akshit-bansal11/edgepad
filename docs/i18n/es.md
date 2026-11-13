# Spanish (es) glossary

Android `values-es`, Windows culture `es`. Neutral international Spanish that reads naturally in Mexico, Spain, Argentina and Colombia. No regionalisms.

## Register and conventions

- **Register:** informal "tú", imperative for actions ("Toca", "Elige", "Escribe"). No "vosotros" and no voseo ("vos"/"tocá"). Where a sentence needs a subject, it is written so it needs no pronoun.
- **Titles and labels:** sentence case ("Configuración de la superficie"). Buttons are infinitive-free verbs in the imperative or short nouns ("Conectar", "Olvidar").
- **Questions and exclamations:** opening ¿ and ¡ always.
- **Quotes:** angle quotes « » for names of menu items and buttons quoted inside a sentence ("Conexiones Wi-Fi y USB").
- **Numbers and units:** a regular space before units (`5 ms`, `60 Hz`, `1 MB`, `120 dp`); `%` is attached to the number (`50%`), as in Latin American UIs and most apps. Decimal handled by the system formatter.
- **Plurals:** one / many / other. The `many` form is for millions ("1 000 000 de segundos") and takes "de" after the number.
- **Gender:** "laptop" is feminine (la laptop, conectada, recordada, activa). "Mac" is masculine ("el Mac").
- **Settings word:** "Configuración" (Windows and Android in Latin America; understood in Spain), not "Ajustes".
- **Key caps:** Ctrl, Alt, Mayús (Shift), Fn, Esc, Supr, Intro, Inicio, Fin, Re Pág, Av Pág, Bloq Mayús. Windows logo key is left as Windows labels it.
- **Dial short labels:** VOL, BRILLO, ZOOM, AVANCE, APPS, HZ, VERT, HORIZ, ESCR, PEST, LAPTOP.

## Terms

| English | Spanish |
|---|---|
| surface, control surface | superficie (de control) |
| dial / edge ruler / ruler | regla |
| corner(s) / split corners | esquina(s) / esquinas divididas |
| volume | volumen |
| brightness | brillo |
| zoom | zoom |
| media scrub | avance de reproducción |
| app switcher | cambiar de app |
| refresh rate | frecuencia de actualización |
| vertical / horizontal scroll | desplazamiento vertical / horizontal |
| virtual desktops, task view | escritorios virtuales, vista de tareas |
| browser tabs | pestañas del navegador |
| switch laptop | cambiar de laptop |
| trackpad | panel táctil |
| pointer | puntero |
| gesture | gesto |
| natural scrolling | desplazamiento natural |
| media, now-playing bar | multimedia, En reproducción |
| Controls | Controles |
| keyboard | teclado |
| function keys: With Fn, Direct | teclas de función: Con Fn, Directo |
| gamepad | gamepad |
| gamepad presets | Xbox / Plataformas / Carreras / Disparos | `gamepad_preset_*`; the guide uses the same words | Xbox keys preset (separate from Xbox): Xbox (teclas).
| layout / preset | diseño / preajuste |
| binding | asignación |
| D-pad / stick / button | cruceta / stick / botón |
| shoulder / trigger | botón superior / gatillo |
| keyboard mode | modo teclado |
| macro, macro deck | macro |
| annotate | anotar |
| sign | firmar |
| capture | captura |
| shapes | formas |
| lock the trackpad / pad | bloquear el panel táctil / el panel |
| share clipboard | compartir portapapeles |
| laptop | laptop |
| phone | teléfono |
| pairing / pair | vinculación / vincular |
| pairing code | código de vinculación |
| PIN | PIN |
| trust / don't trust | confiar / no confiar |
| trusted phone | teléfono de confianza |
| security level | nivel de seguridad |
| forget | olvidar |
| remembered | recordada |
| Devices | Dispositivos |
| active (laptop) / make active | activa / activar |
| connect, disconnect, reconnect | conectar, desconectar, reconectar |
| handshake | saludo inicial |
| link, connection | conexión |
| Nearby devices | Dispositivos cercanos |
| USB tethering | Compartir conexión por USB |
| haptic feedback | respuesta háptica |
| appearance / Look | Apariencia / Aspecto |
| theme: System, Dark, Light | Sistema, Oscuro, Claro |
| upright / sideways | vertical / horizontal |
| control colour | color de los controles |
| tray app, notification area | área de notificaciones |
| tray menu | menú del área de notificaciones |
| Wi-Fi and USB connections | Conexiones Wi-Fi y USB |
| firewall | Firewall (Firewall de Windows Defender) |
| start with Windows | Iniciar con Windows |
| update, release notes, release page, major release | actualizar, notas de la versión, página de la versión, versión principal |
| protocol | protocolo |
| round trip | tiempo de respuesta |
| Macros… menu item | Macros… (same text on phone and laptop) |

## Decisions

- "Laptop" over "portátil" (Spain) or "computadora" (Latin America): the most widely understood single word in both, kept feminine (la laptop, as in Latin American usage). Confirmed in the second review pass. For Bluetooth devices that identify as a computer, "equipo" is used, also in "Mostrar solo equipos".
- "Gamepad" over "mando" (Spain) or "control" (Latin America), for the same reason. It is the only word used, in sentences too ("un gamepad", never "un mando").
- "Teléfono" over "móvil" (Spain) or "celular" (Latin America).
- "Añadir" over "agregar": both are neutral; "añadir" is used throughout.
- The dial is called "regla" in every place, to keep the ruler-with-ticks picture.

## Laptop app (Windows resx)

- Menu and button names quoted by the Android text match exactly: «Macros…», «Conexiones Wi-Fi y USB», «Compartir portapapeles», Confiar / No confiar, Olvidar, Restablecer.
- Other laptop terms: Seguridad, Definir PIN, Olvidar todos, Restablecer PIN y olvidar teléfonos, Nivel {0}, Solo confianza, Teléfonos de confianza, Iniciar con Windows, Buscar actualizaciones a diario, Abrir registro (log), Permitir en el Firewall de Windows, área de notificaciones (tray).
- Access keys, unique per menu: tray menu n M S W F B P D R A U L; Security window P O R G U; Macros window A E I U Q G.
- Arrow keys in shortcuts are written out: Alt+Arriba, Alt+Abajo.
- Pairing warning keeps "Vincula solo un teléfono que sea tuyo" plus Cancelar.
