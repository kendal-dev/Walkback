# Walkback · las láminas de la defensa

Rama `pitch`. La hace evolucionar **Josué** a medida que avanza la app. Se arma desde cero (no es la plantilla de la práctica): sigue el plan lámina por lámina del guion del sábado §22.4.

## Cómo se abre

- Doble clic en `index.html`, o mejor con un servidor local (las fuentes cargan seguro):
  `python -m http.server 8765 --directory pitch` → `http://localhost:8765`
- Todo funciona **sin internet**: la letra Inter y las imágenes están en la carpeta.

## Teclas

| Tecla | Qué hace |
|---|---|
| → · Espacio · clic a la derecha | Lámina siguiente |
| ← · clic a la izquierda | Lámina anterior |
| **N** | Notas: lo que dice Daniel, los avisos ⚠️ y lo que falta |
| **T** | Prende y apaga el reloj |
| **Q** | En la lámina 1, escribe la pregunta ya (si no, sale sola a los 20 s) |
| **F** | Pantalla completa |

## Cómo se actualiza

1. **Los números van en `data.js`, y en ningún otro lado.** Un valor en `null` se muestra como «?» o se esconde, y aparece en las notas (N) como pendiente.
   - `straightMinutes` y `safeMinutes` (lámina 2): de la app, cuando el modelo de caminata esté conectado. **Hoy el panel muestra valores de prueba: no se copian.**
   - `btuConfirmed` (lámina 5): pasa a `true` solo cuando `docs/SOURCES.md` tenga el enlace de los 1.200 BTU/hr.
   - `rockLimitM`: una sola cifra (1 m o 4 m), la confirma la sesión B.
   - `testers` y `testerScore`: después de las pruebas del domingo a las 8:30.
2. **Las capturas van en `img/`.** Hoy son las de la prueba automática (`TestOutput/` del proyecto). Cambiarlas por las del visor cuando estén:
   - Lámina 3, tarjeta MISSION: la captura del HUD (hoy, la ruta a la altura de los ojos).
   - Lámina 7: el astronauta 3D sobre una cresta (hoy, la foto de Nevado Sajama).
   - Lámina 8: `img/qr.png` con el QR al repositorio (aparece solo si el archivo existe).
3. **Reglas** (guion §22.4 y §11.4): inglés en pantalla · títulos de 60 px o más, texto de 28 px o más · una idea por lámina · **sin logos de la NASA** · créditos legibles · nada que se cargue de internet.
4. **Que no se contradigan con la app:** el domingo, antes del ensayo, revisar cada número contra la app.

## Créditos de las imágenes

- Nevado Sajama: Curiosity Mastcam, PIA26696, NASA/JPL-Caltech/MSSS.
- El globo: Viking MDIM 2.1, NASA/JPL/USGS.
- El terreno y la foto de base: MRO HiRISE (NASA Mars Trek y USGS), NASA/JPL/University of Arizona.
- Capturas de Walkback: el equipo MARSXR.
- Letra Inter: SIL Open Font License (`fonts/LICENSE.txt`).

Hecho con Claude Code (IA), declarado en la entrega.
