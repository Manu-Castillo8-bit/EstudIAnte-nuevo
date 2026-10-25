# EstudIAnte 📚🤖

**EstudIAnte** es una aplicación diseñada para transformar la manera en que estudias e interactúas con tus documentos académicos y de investigación.

---

## 🎯 Objetivo del Proyecto

El objetivo principal de **EstudIAnte** es ayudarte a procesar los documentos que subas a la aplicación para generar resúmenes automáticos y precisos. 

A partir del contenido de tus archivos, la aplicación actúa como un **tutor interactivo** que:
- **Te hace preguntas** clave para evaluar y reforzar tu comprensión del tema.
- **Responde a tus dudas** y preguntas específicas sobre el documento, ayudándote a entender mejor cualquier concepto complejo.
- **Genera cuestionarios** de 5, 10, 15 o 20 preguntas a partir de tus documentos o chats.
- **Crea diagramas** (flujos y mapas mentales Mermaid) a partir de tu material de estudio.

---

## 🤖 Inteligencia Artificial (multi-proveedor con respaldo)

La app no llama a ninguna API directamente: usa una **Edge Function de Supabase** que mantiene las claves como secretos en el servidor, así la IA funciona en cualquier clon del repo sin configurar nada.

**Cadena de IAs y comportamiento ante saturación** (siempre se intenta primero Gemini):
1. **Google Gemini** (servidor) → 2. **Groq / GPT-OSS** (servidor) → 3. **OpenRouter, modelos gratuitos** (servidor) → 4. **Gemini local** (solo si pegas tu propia clave en `api-key-gemini.txt` o la variable `ESTUDIANTE_GEMINI_KEY`).

Si una IA está **saturada** (cuota agotada), la app muestra un **popup**: *"¿Quieres cambiar a Groq?"* — el usuario decide si cambia a la siguiente IA (y así sucesivamente) o prefiere esperar. El servidor nunca cambia de proveedor por su cuenta: la app le pide un proveedor concreto en cada petición.

### Despliegue / actualización de la función
```bash
# Claves (Groq es gratis: https://console.groq.com/keys)
supabase secrets set GEMINI_API_KEY=tu_clave_gemini GROQ_API_KEY=tu_clave_groq

# Desplegar la función (después de cualquier cambio en supabase/functions/gemini)
supabase functions deploy gemini
```
Opcionalmente puedes ajustar los modelos con los secretos `GEMINI_MODEL` y `GROQ_MODEL`.

### Esquema de base de datos
Si tu proyecto de Supabase ya existía antes de la tabla `diagramas`, ejecuta en el SQL Editor las líneas de `supabase/schema.sql` correspondientes a **DIAGRAMAS** (tabla + índice).

---

## 👥 Desarrollado por NEXO

Este proyecto fue desarrollado por el equipo **NEXO**:

* **Manuel Ernesto Castillo Guerra** — *N° 17*
* **Daniel Ernesto Claros Orellana** — *N° 18*
* **Mayeli Abigail Villalobos Guardado** — *N° 12*
* **René Antonio Orellana García** — *N° 27*