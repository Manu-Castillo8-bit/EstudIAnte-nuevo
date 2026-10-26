// Edge Function: gemini
// Intermediario seguro hacia las IAs. Las API keys viven como SECRETOS en Supabase
// (nunca en el repositorio), así cualquiera puede clonar la app sin configurar nada.
//
// La APP elige la IA de cada petición: envía { prompt, proveedor } donde
//   proveedor = "gemini" (por defecto) | "groq"
// El servidor llama SOLO a ese proveedor. Si está saturado o falla tras sus
// reintentos internos, responde con "saturada": true para que la APP pregunte
// al usuario si quiere cambiar a otra IA (popup). El servidor NUNCA cambia de
// proveedor por su cuenta.
//
// Secretos (Supabase Dashboard -> Edge Functions -> Secrets):
//   GEMINI_API_KEY     = clave de https://aistudio.google.com/app/apikey
//   GEMINI_MODEL       = (opcional) por defecto "gemini-3.8-flash"
//   GROQ_API_KEY       = clave gratuita de https://console.groq.com/keys (alternativa 1)
//   GROQ_MODEL         = (opcional) por defecto "openai/gpt-oss-120b"
//   OPENROUTER_API_KEY = clave de https://openrouter.ai/keys (alternativa 2, modelos gratuitos)
//   OPENROUTER_MODEL   = (opcional) por defecto "nvidia/nemotron-3.5-lightning:free"
//
// Despliegue: supabase functions deploy gemini

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const MODELO_GEMINI = Deno.env.get("GEMINI_MODEL") ?? "gemini-3.8-flash";
const MODELO_GROQ = Deno.env.get("GROQ_MODEL") ?? "openai/gpt-oss-120b";
const MODELO_OPENROUTER =
  Deno.env.get("OPENROUTER_MODEL") ?? "nvidia/nemotron-3.5-lightning:free";

const REINTENTOS = 3;

/// Límite de tiempo por cada intento de llamada a una IA (evita workers colgados
/// por "WORKER_RESOURCE_LIMIT" cuando un proveedor tarda demasiado en responder).
const TIEMPO_POR_INTENTO_MS = 30000;

class IAError extends Error {
  saturada: boolean;
  constructor(mensaje: string, saturada: boolean) {
    super(mensaje);
    this.saturada = saturada;
  }
}

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: CORS });
  }

  try {
    const { prompt, proveedor } = await req.json();
    if (!prompt || typeof prompt !== "string") {
      return json({ error: "Falta el campo 'prompt'." }, 400);
    }

    const elegido = proveedor === "groq"
      ? "groq"
      : proveedor === "openrouter"
        ? "openrouter"
        : "gemini";

    try {
      const texto = elegido === "groq"
        ? await llamarGroq(prompt)
        : elegido === "openrouter"
          ? await llamarOpenRouter(prompt)
          : await llamarGemini(prompt);

      return json({
        texto,
        modelo: elegido === "groq"
          ? MODELO_GROQ
          : elegido === "openrouter"
            ? MODELO_OPENROUTER
            : MODELO_GEMINI,
        proveedor: elegido,
      });
    } catch (e) {
      if (e instanceof IAError) {
        return json(
          { error: `${elegido}: ${e.message}`, saturada: e.saturada },
          e.saturada ? 429 : 502,
        );
      }
      return json({ error: `${elegido}: ${String(e)}` }, 502);
    }
  } catch (e) {
    return json({ error: `Error inesperado: ${e}` }, 500);
  }
});

// ---------------- Gemini (API de Google AI Studio) ----------------

async function llamarGemini(prompt: string): Promise<string> {
  const apiKey = Deno.env.get("GEMINI_API_KEY");
  if (!apiKey) {
    throw new IAError("El servidor no tiene configurada GEMINI_API_KEY.", false);
  }

  const url =
    `https://generativelanguage.googleapis.com/v1beta/models/${MODELO_GEMINI}:generateContent?key=${apiKey}`;
  const cuerpo = { contents: [{ parts: [{ text: prompt }] }] };

  let respuesta: Response | null = null;
  let detalle = "";

  for (let intento = 1; intento <= REINTENTOS; intento++) {
    try {
      respuesta = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cuerpo),
        signal: AbortSignal.timeout(TIEMPO_POR_INTENTO_MS),
      });
    } catch {
      // Timeout o error de red: se trata como saturación para que la app
      // ofrezca al usuario cambiar de IA.
      detalle = "timeout esperando respuesta";
      if (intento === REINTENTOS) break;
      await new Promise((r) => setTimeout(r, intento * 1000));
      continue;
    }

    if (respuesta.ok) break;

    detalle = await respuesta.text();

    const saturado = respuesta.status === 500 ||
      respuesta.status === 503 ||
      respuesta.status === 529;

    if (!saturado || intento === REINTENTOS) break;

    await new Promise((r) => setTimeout(r, intento * 1000));
  }

  if (!respuesta || !respuesta.ok) {
    const estado = respuesta?.status ?? 0;
    const saturada = estado === 429 || estado === 500 || estado === 503 ||
      estado === 529 || /quota|RESOURCE_EXHAUSTED|timeout/i.test(detalle);
    throw new IAError(
      `respondió ${estado || "sin respuesta"}. ${detalle.slice(0, 200)}`,
      saturada,
    );
  }

  const data = await respuesta.json();
  const partes = data?.candidates?.[0]?.content?.parts ?? [];
  const texto = partes.map((p: { text?: string }) => p.text ?? "").join("");

  if (!texto.trim()) throw new IAError("devolvió una respuesta vacía.", false);
  return texto;
}

// ---------------- Groq (API compatible con OpenAI) ----------------

async function llamarGroq(prompt: string): Promise<string> {
  const apiKey = Deno.env.get("GROQ_API_KEY");
  if (!apiKey) {
    throw new IAError("El servidor no tiene configurada GROQ_API_KEY.", false);
  }

  return await llamarOpenAICompatible(
    "https://api.groq.com/openai/v1/chat/completions",
    apiKey,
    MODELO_GROQ,
    prompt,
    {},
  );
}

// ---------------- OpenRouter (API compatible con OpenAI) ----------------

async function llamarOpenRouter(prompt: string): Promise<string> {
  const apiKey = Deno.env.get("OPENROUTER_API_KEY");
  if (!apiKey) {
    throw new IAError("El servidor no tiene configurada OPENROUTER_API_KEY.", false);
  }

  return await llamarOpenAICompatible(
    "https://openrouter.ai/api/v1/chat/completions",
    apiKey,
    MODELO_OPENROUTER,
    prompt,
    {
      "HTTP-Referer": "https://estudiante.app",
      "X-Title": "EstudIAnte",
    },
  );
}

/// Llamada genérica a una API compatible con OpenAI (Groq, OpenRouter, etc.).
async function llamarOpenAICompatible(
  url: string,
  apiKey: string,
  modelo: string,
  prompt: string,
  cabecerasExtra: Record<string, string>,
): Promise<string> {
  const cuerpo = {
    model: modelo,
    messages: [{ role: "user", content: prompt }],
    temperature: 0.7,
  };

  let respuesta: Response | null = null;
  let detalle = "";

  for (let intento = 1; intento <= REINTENTOS; intento++) {
    try {
      respuesta = await fetch(url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Authorization": `Bearer ${apiKey}`,
          ...cabecerasExtra,
        },
        body: JSON.stringify(cuerpo),
        signal: AbortSignal.timeout(TIEMPO_POR_INTENTO_MS),
      });
    } catch {
      // Timeout o error de red: se trata como saturación para que la app
      // ofrezca al usuario cambiar de IA.
      detalle = "timeout esperando respuesta";
      if (intento === REINTENTOS) break;
      await new Promise((r) => setTimeout(r, intento * 1000));
      continue;
    }

    if (respuesta.ok) break;

    detalle = await respuesta.text();

    const reintenable = respuesta.status === 429 || respuesta.status >= 500;

    if (!reintenable || intento === REINTENTOS) break;

    await new Promise((r) => setTimeout(r, intento * 1000));
  }

  if (!respuesta || !respuesta.ok) {
    const estado = respuesta?.status ?? 0;
    const saturada = (estado >= 500 && estado < 600) ||
      /rate.?limit|quota|429|timeout/i.test(detalle) || estado === 429;
    throw new IAError(
      `respondió ${estado || "sin respuesta"}. ${detalle.slice(0, 200)}`,
      saturada,
    );
  }

  const data = await respuesta.json();
  const texto: string = data?.choices?.[0]?.message?.content ?? "";

  if (!texto.trim()) throw new IAError("devolvió una respuesta vacía.", false);
  return texto;
}

function json(obj: unknown, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}
