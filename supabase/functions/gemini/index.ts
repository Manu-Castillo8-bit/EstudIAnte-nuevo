// Edge Function: gemini
// Actúa como intermediario seguro hacia Google Gemini.
// La API key de Gemini vive como SECRETO en Supabase (nunca en el repositorio),
// de modo que cualquiera puede clonar la app y la IA funciona sin pegar una clave.
//
// Secretos necesarios (Supabase Dashboard -> Edge Functions -> Secrets):
//   GEMINI_API_KEY   = tu clave de https://aistudio.google.com/app/apikey
//   GEMINI_MODEL     = (opcional) por defecto "gemini-3.8-flash"

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const MODELO = Deno.env.get("GEMINI_MODEL") ?? "gemini-3.8-flash";

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: CORS });
  }

  try {
    const apiKey = Deno.env.get("GEMINI_API_KEY");
    if (!apiKey) {
      return json({ error: "El servidor no tiene configurada GEMINI_API_KEY." }, 500);
    }

    const { prompt } = await req.json();
    if (!prompt || typeof prompt !== "string") {
      return json({ error: "Falta el campo 'prompt'." }, 400);
    }

    const url =
      `https://generativelanguage.googleapis.com/v1beta/models/${MODELO}:generateContent?key=${apiKey}`;

    const cuerpo = {
      contents: [{ parts: [{ text: prompt }] }],
    };

    let respuesta: Response | null = null;
    let detalle = "";

    for (let intento = 1; intento <= 3; intento++) {
      respuesta = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cuerpo),
      });

      if (respuesta.ok) break;

      detalle = await respuesta.text();

      const saturado = respuesta.status === 503 ||
        respuesta.status === 500 ||
        respuesta.status === 529;

      if (!saturado || intento === 3) break;

      await new Promise((r) => setTimeout(r, intento * 1000));
    }

    if (!respuesta || !respuesta.ok) {
      return json(
        { error: `Gemini respondió ${respuesta?.status ?? "sin respuesta"}. ${detalle.slice(0, 200)}` },
        502,
      );
    }

    const data = await respuesta.json();
    const partes = data?.candidates?.[0]?.content?.parts ?? [];
    const texto = partes.map((p: { text?: string }) => p.text ?? "").join("");

    if (!texto.trim()) {
      return json({ error: "Gemini devolvió una respuesta vacía." }, 502);
    }

    return json({ texto, modelo: MODELO });
  } catch (e) {
    return json({ error: `Error inesperado: ${e}` }, 500);
  }
});

function json(obj: unknown, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}
