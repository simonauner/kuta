# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Köra lokalt

Tre terminaler, från repots rot. Allt lokalt ligger på portar i 553xx-serien.

```bash
supabase start                                  # Postgres + PostGIS + Auth + Mailpit
dotnet run --project api/Kuta.Api               # API på http://localhost:55302 (kör migreringar vid start)
cd web && nvm use && npm install && npm run dev # webben på http://localhost:55300, /api proxas till API:t
```

| Vad | Adress |
|---|---|
| Webben | http://localhost:55300 |
| API | http://localhost:55302 |
| Mailpit (bekräftelsemejl) | http://127.0.0.1:55324 |
| Supabase Studio | http://127.0.0.1:55323 |

Kommer bekräftelsemejlet på engelska med en länk till Supabase i stället för till `localhost:55300/api/auth/confirm` har auth-containern startat före Kong: `docker restart supabase_auth_kuta` och registrera igen.

Tester (kräver `supabase start`): `dotnet test --project api/Kuta.Api.Tests`.
