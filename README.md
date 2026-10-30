# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Köra lokalt

```bash
supabase start                             # Postgres + PostGIS + Auth (Supabase CLI)
dotnet run --project api/Kuta.Api          # API på http://localhost:5080
cd web && npm install && npm run dev       # webben, /api proxas till API:t
```
