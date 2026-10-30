# Kuta

Kuta (kuta.io) är en Strava-liknande löparapp: ladda upp pass från Garmin (FIT) eller logga manuellt, följ statistik över tid (puls, fart, max distans), logga skor per pass, få rekord ("snabbaste 5k", "snabbaste 5k i år"), och följ andra löpare med flöde, profiler och grupper. Svenskt namn, ska funka internationellt. Webbapp som PWA (sparas till hemskärmen) – ingen App Store i början.

## Stack (beslutad)
- **API:** .NET 10, ASP.NET Core minimal API, **Dapper** (inte EF Core), Npgsql, Postgres + PostGIS, NetTopologySuite.
- **Supabase:** hanterad Postgres (EU-region) och **Supabase Auth**. Inloggningen går via vårt API (BFF, samma mönster som Kredovis): API:t pratar med Supabase Auth och lägger sessionen i en krypterad HttpOnly-cookie, så tokens aldrig når JavaScript. Webben använder varken supabase-js eller Supabases Data API och behöver inga Supabase-nycklar. Lokalt: `supabase start` (portar 553xx, se `supabase/config.toml`).
- **Migreringar:** DbUp med inbäddade SQL-filer i `api/Kuta.Api/Migrations/`.
- **Web:** React + Vite + TypeScript, TanStack Router (kodbaserad) + Query, Tailwind v4, shadcn/ui (components.json finns, komponenter läggs till med `npx shadcn@latest add ...`), Motion för animationer, vite-plugin-pwa, MapLibre GL.
- **Filer:** Cloudflare R2 (S3-kompatibelt) för rå-FIT-filer och PMTiles-kartor. `activities.raw_file_key` pekar på objektnyckeln.
- **Jobb/köer:** börja med Postgres-baserad kö (Hangfire eller Wolverine), inte Redis/RabbitMQ. E-post och web push kommer senare.
- **FIT-parsning:** Garmins officiella C#-SDK (NuGet `Garmin.FIT.Sdk`).

## Status
- `web/`: bygger (`npm run build` OK). Skal med routes Flöde/Pass/Statistik/Profil, bottennavigering, sidövergångar, PWA-manifest. PNG-ikoner (192/512/maskable, apple-touch-icon) saknas.
- `api/`: bygger och körs mot lokal Supabase; `/api/health` svarar med PostGIS-version. Paketversioner pinnade. Namnmatchning snake_case→PascalCase är satt via `DefaultTypeMap.MatchNamesWithUnderscores`.
- Auth (portad och slimmad från Kredovis `SupabaseAuthClient`/`SupabaseCookieSession`):
  - `POST /api/auth/signup|login|logout`, `GET /api/auth/confirm?token_hash=` (länken i bekräftelsemejlet, mall i `supabase/templates/confirmation.html`), `GET /api/antiforgery`.
  - Alla skrivande `/api`-anrop kräver `X-CSRF-TOKEN` från `GET /api/antiforgery`. Token är knuten till användaren: hämta en ny efter in- och utloggning.
  - Sessioner i `user_sessions` (kontrolleras vid varje anrop, utloggning slår igenom direkt). Cookien glider 30 dagar, hård gräns 180 dagar. Access-token förnyas strax före utgång; Supabase tillfrågas om återkallelse högst var 5:e minut.
  - Data Protection-nycklar i `data_protection_keys`. Rate limit 10 auth-anrop/5 min/IP.
  - `users` är en profiltabell med samma id som `auth.users` (FK, kaskadradering). `GET /api/me` ger id, e-post och `displayName` (null tills profilen skapats); `PUT /api/me` skapar/uppdaterar profilen.
  - Saknas: lösenordsåterställning, MFA, inloggning med Google/Apple, logga ut andra sessioner, städning av gamla sessionsrader, Turnstile.
  - Lokalt: kommer bekräftelsemejlet i Mailpit (http://127.0.0.1:55324) med Supabases engelska standardmall har auth-containern startat före Kong – kör `docker restart supabase_auth_kuta`.
- Migreringar körda: 001–003. Ändra aldrig en körd migrering – nya ändringar i nästa nummer.
- Privat repo: github.com/simonauner/kuta. Node 24 (`.nvmrc`).
- Inget hostat Supabase-projekt än. Vid uppsättning: EU-region, stäng av Data API i dashboarden (grants är redan återkallade, se nedan), lägg in bekräftelsemallen och minsta lösenordslängd 8 i dashboarden, och sätt via miljövariabler: `ConnectionStrings__Default` (session pooler, port 5432, `Maximum Pool Size` under poolstorleken), `Supabase__Url`, `Supabase__PublishableKey` och `Supabase__SecretKey` (gör att besökarens IP skickas vidare till Supabase). Före produktion: skydda Data Protection-nycklarna med certifikat och konfigurera forwarded headers så att rate limiten ser klientens IP.

## Designbeslut att hålla fast vid
- **Tidsserier som arrayer** i `activity_streams` (en rad per pass), inte en rad per sekund.
- **Importen är en idempotent pipeline:** spara rå fil → parsa → sammanfattning + förenklad rutt (Douglas-Peucker) → bästa insatser per standarddistans (1 km, 1 mile, 5k, 10k, halvmara, mara) → uppdatera personbästa. Ska gå att köra om vid ändrad logik. "Snabbaste 5k i år" är en query över `best_efforts`.
- **Garmin Connect Developer Program är pausat för nya ansökare** → bygg manuell FIT/GPX-uppladdning först, automatisk Garmin-synk senare via en import-abstraktion. Inga inofficiella Garmin-scrapers.
- **Strava som datakälla är uteslutet** (deras API-villkor förbjuder att visa data för andra än användaren).
- **Integritet/GDPR:** puls är hälsodata → uttryckligt samtycke. Pass privata som standard (`privacy = 'private'`), dolda start-/slutzoner på rutter, export och radering av konto från start. Personuppgiftsansvarig är användarens AB.
- Segment/topplistor à la Strava: avvakta, inte v1.
- **Enheter:** SI i databas och API (meter, sekunder, m/s; fältnamn med enhetssuffix som `distanceM`, `durationS`). Metriskt/imperial är ett visningsval per användare och konverteras bara i webben.
- **Data API är stängt:** migrering 003 återkallar `anon`/`authenticated` från allt i `public`, även framtida tabeller (default privileges). Lägg inte tillbaka grants; behövs direktåtkomst från webben senare, gör det medvetet med RLS-policies.
- **Milen är 1609,344 m.** `best_efforts.distance_m = 1609` är bara en etikett; rekordmotorn räknar med exakt längd.

## Nästa steg (i ordning)
1. ~~GitHub-repo~~, ~~bygga och köra API:t~~, ~~auth i API:t (Supabase)~~ – klart.
2. Webb: inloggnings-/registreringssidor mot `/api/auth/*` (med CSRF-token), profilnamn via `PUT /api/me`.
3. Hostat Supabase-projekt (EU) och deploy av API:t.
4. FIT-uppladdning: endpoint, lagring i R2, parsning med `Garmin.FIT.Sdk`, skriv `activities` + `activity_streams`.
5. Rekordmotor (glidande fönster över distans/tid-strömmen), tester med riktiga FIT-filer.
6. Webb: uppladdning, passsida med karta + grafer (Recharts, uPlot för långa serier), statistikvyer, skor.
7. Därefter: följare, flöde, grupper; e-post och push.

## Konventioner
- Svenska i UI-texter, engelska i kod och commit-meddelanden.
- JSON i API:t är lowerCamelCase. Returnera typade records från Dapper, aldrig `dynamic`-rader (de serialiseras med snake_case-kolumnnamn som nycklar).
- Små, fokuserade commits. Kör `npm run build` i `web/` och `dotnet test --project api/Kuta.Api.Tests` (kräver `supabase start`) innan commit.
