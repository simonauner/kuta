# Kuta

Kuta (kuta.io) är en Strava-liknande löparapp: ladda upp pass från Garmin (FIT) eller logga manuellt, följ statistik över tid (puls, fart, max distans), logga skor per pass, få rekord ("snabbaste 5k", "snabbaste 5k i år"), och följ andra löpare med flöde, profiler och grupper. Svenskt namn, ska funka internationellt. Webbapp som PWA (sparas till hemskärmen) – ingen App Store i början.

## Stack (beslutad)
- **API:** .NET 10, ASP.NET Core minimal API, **Dapper** (inte EF Core), Npgsql, Postgres + PostGIS, NetTopologySuite.
- **Migreringar:** DbUp med inbäddade SQL-filer i `api/Kuta.Api/Migrations/`.
- **Web:** React + Vite + TypeScript, TanStack Router (kodbaserad) + Query, Tailwind v4, shadcn/ui (components.json finns, komponenter läggs till med `npx shadcn@latest add ...`), Motion för animationer, vite-plugin-pwa, MapLibre GL.
- **Filer:** Cloudflare R2 (S3-kompatibelt) för rå-FIT-filer och PMTiles-kartor. `activities.raw_file_key` pekar på objektnyckeln.
- **Jobb/köer:** börja med Postgres-baserad kö (Hangfire eller Wolverine), inte Redis/RabbitMQ. E-post och web push kommer senare.
- **FIT-parsning:** Garmins officiella C#-SDK (NuGet `Dynastream.Fit`).

## Status
- `web/`: bygger (`npm run build` OK). Skal med routes Flöde/Pass/Statistik/Profil, bottennavigering, sidövergångar, PWA-manifest. PNG-ikoner (192/512/maskable, apple-touch-icon) saknas.
- `api/`: **skrivet men aldrig kompilerat eller körts** (skapades i en miljö utan .NET SDK). Paketversioner är flytande (`10.*` osv.) – verifiera, pinna. Namnmatchning snake_case→PascalCase är satt via `DefaultTypeMap.MatchNamesWithUnderscores`.
- `001_init.sql` är inte körd. Granska schemat innan första körning.
- Inget GitHub-repo skapat än.

## Designbeslut att hålla fast vid
- **Tidsserier som arrayer** i `activity_streams` (en rad per pass), inte en rad per sekund.
- **Importen är en idempotent pipeline:** spara rå fil → parsa → sammanfattning + förenklad rutt (Douglas-Peucker) → bästa insatser per standarddistans (1 km, 1 mile, 5k, 10k, halvmara, mara) → uppdatera personbästa. Ska gå att köra om vid ändrad logik. "Snabbaste 5k i år" är en query över `best_efforts`.
- **Garmin Connect Developer Program är pausat för nya ansökare** → bygg manuell FIT/GPX-uppladdning först, automatisk Garmin-synk senare via en import-abstraktion. Inga inofficiella Garmin-scrapers.
- **Strava som datakälla är uteslutet** (deras API-villkor förbjuder att visa data för andra än användaren).
- **Integritet/GDPR:** puls är hälsodata → uttryckligt samtycke. Pass privata som standard (`privacy = 'private'`), dolda start-/slutzoner på rutter, export och radering av konto från start. Personuppgiftsansvarig är användarens AB.
- Segment/topplistor à la Strava: avvakta, inte v1.

## Nästa steg (i ordning)
1. Skapa privat GitHub-repo `kuta`, initiera git, första commit.
2. `docker compose up -d`, `dotnet restore && dotnet build`, `dotnet run --project api/Kuta.Api`; verifiera `/api/health` (ska visa PostGIS-version) och att migreringen går igenom. Fixa det som inte kompilerar.
3. Auth (börja enkelt, t.ex. ASP.NET Core Identity eller passkeys) och ersätt `userId`-parametern i `/api/activities`.
4. FIT-uppladdning: endpoint, lagring i R2, parsning med `Dynastream.Fit`, skriv `activities` + `activity_streams`.
5. Rekordmotor (glidande fönster över distans/tid-strömmen), tester med riktiga FIT-filer.
6. Webb: uppladdning, passsida med karta + grafer (Recharts, uPlot för långa serier), statistikvyer, skor.
7. Därefter: följare, flöde, grupper; e-post och push.

## Konventioner
- Svenska i UI-texter, engelska i kod och commit-meddelanden.
- Små, fokuserade commits. Kör `npm run build` i `web/` och `dotnet build` innan commit.
