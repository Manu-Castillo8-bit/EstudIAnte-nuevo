-- ============================================================
-- EstudIAnte - Esquema de base de datos para Supabase (PostgreSQL)
-- Ejecutar en: Supabase Dashboard > SQL Editor > New query > Run
-- ============================================================

-- ---------- USUARIOS ----------
-- Contraseñas en texto plano (requisito del proyecto para poder
-- consultarlas si el usuario la olvida). No usar datos reales.
create table if not exists usuarios (
    id           text primary key,
    usuario      text not null unique,
    email        text unique,
    password     text not null,
    nombre       text not null default '',
    rol          text not null default 'estudiante'
                 check (rol in ('admin', 'estudiante')),
    creado_en    timestamptz not null default now(),
    actualizado_en timestamptz not null default now()
);

-- ---------- DOCUMENTOS ----------
create table if not exists documentos (
    id            text primary key,
    usuario_id    text not null references usuarios (id) on delete cascade,
    nombre        text not null,
    tipo          text not null check (tipo in ('pdf', 'docx', 'txt')),
    tamano_bytes  bigint not null default 0,
    ruta_storage  text,                -- ruta del archivo en Supabase Storage (bucket 'documentos')
    resumen       text not null default '',
    texto         text not null default '',   -- texto extraido para que la IA haga cuestionarios
    estado        text not null default 'subido'
                  check (estado in ('subido', 'analizando', 'listo', 'error')),
    creado_en     timestamptz not null default now()
);

-- ---------- TEMAS DE CHAT ----------
create table if not exists temas (
    id         text primary key,
    usuario_id text not null references usuarios (id) on delete cascade,
    nombre     text not null,
    creado_en  timestamptz not null default now()
);

-- ---------- CHATS ----------
create table if not exists chats (
    id         text primary key,
    usuario_id text not null references usuarios (id) on delete cascade,
    tema       text not null,          -- Matematicas, Programacion, Ciencias, Historia...
    titulo     text not null default 'Nueva conversacion',
    creado_en  timestamptz not null default now()
);

-- ---------- MENSAJES ----------
create table if not exists mensajes (
    id         text primary key,
    chat_id    text not null references chats (id) on delete cascade,
    rol        text not null check (rol in ('user', 'assistant')),
    contenido  text not null,
    creado_en  timestamptz not null default now()
);

-- ---------- CUESTIONARIOS ----------
create table if not exists cuestionarios (
    id              text primary key,
    documento_id    text references documentos (id) on delete set null,
    usuario_id      text not null references usuarios (id) on delete cascade,
    titulo          text not null,
    nivel           text not null default 'Medio'
                    check (nivel in ('Basico', 'Medio', 'Alto')),
    total_preguntas int not null default 5,
    tiempo_limite   int not null default 10,   -- minutos
    creado_en       timestamptz not null default now()
);

-- ---------- PREGUNTAS ----------
create table if not exists preguntas (
    id                 text primary key,
    cuestionario_id    text not null references cuestionarios (id) on delete cascade,
    enunciado          text not null,
    opcion_a           text not null,
    opcion_b           text not null,
    opcion_c           text not null,
    opcion_d           text not null,
    respuesta_correcta text not null check (respuesta_correcta in ('A', 'B', 'C', 'D')),
    tip                text not null default '',
    fuente             text not null default ''
);

-- ---------- DIAGRAMAS ----------
create table if not exists diagramas (
    id           text primary key,
    documento_id text references documentos (id) on delete set null,
    usuario_id   text not null references usuarios (id) on delete cascade,
    titulo       text not null,
    codigo       text not null,         -- codigo Mermaid del diagrama
    tipo         text not null default 'flujo'
                 check (tipo in ('flujo', 'mapa')),
    fuente       text not null default '',   -- "Documento: X" o "Tema: Y"
    creado_en    timestamptz not null default now()
);

-- ---------- RESULTADOS DE CUESTIONARIOS ----------
create table if not exists resultados (
    id              text primary key,
    cuestionario_id text not null references cuestionarios (id) on delete cascade,
    usuario_id      text not null references usuarios (id) on delete cascade,
    puntaje         int not null default 0,
    total           int not null default 0,
    tiempo_segundos int not null default 0,
    fecha           timestamptz not null default now()
);

-- ---------- INDICES ----------
create index if not exists idx_documentos_usuario  on documentos (usuario_id);
create index if not exists idx_temas_usuario       on temas (usuario_id);
create index if not exists idx_chats_usuario       on chats (usuario_id);
create index if not exists idx_mensajes_chat       on mensajes (chat_id);
create index if not exists idx_cuestionarios_user  on cuestionarios (usuario_id);
create index if not exists idx_preguntas_cuestion  on preguntas (cuestionario_id);
create index if not exists idx_diagramas_usuario   on diagramas (usuario_id);
create index if not exists idx_resultados_usuario  on resultados (usuario_id);

-- ============================================================
-- STORAGE: bucket para archivos subidos
-- ============================================================
insert into storage.buckets (id, name, public, file_size_limit)
values ('documentos', 'documentos', false, 20971520)   -- 20 MB
on conflict (id) do nothing;

-- Politica de acceso al bucket (proyecto escolar: acceso total).
-- Nota: las tablas NO tienen RLS habilitado a proposito, para que
-- la app pueda leer/escribir con la anon key.
drop policy if exists "acceso total documentos" on storage.objects;
create policy "acceso total documentos" on storage.objects
for all using (bucket_id = 'documentos')
with check (bucket_id = 'documentos');

-- ============================================================
-- USUARIO ADMIN DE EJEMPLO (cambiar password en produccion)
-- ============================================================
insert into usuarios (id, usuario, email, password, nombre, rol)
values ('a0000000-0000-0000-0000-000000000001', 'admin', 'admin@estudiante.com', 'admin123', 'Administrador', 'admin')
on conflict (usuario) do nothing;
