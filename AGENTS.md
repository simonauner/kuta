# Kuta

Kuta är en Strava-liknande löparapp: ladda upp pass från Garmin (FIT) eller logga manuellt, följ statistik över tid (puls, fart, max distans), logga skor per pass, få rekord ("snabbaste 5k", "snabbaste 5k i år"), och följ andra löpare med flöde, profiler och grupper. Svenskt namn, ska funka internationellt. Webbapp som PWA (sparas till hemskärmen) – ingen App Store i början.

## Arbetsflöde
- Håll bestående projektregler i `AGENTS.md`, körinstruktioner i `README.md` och återanvändbara agentprocedurer i `.agents/skills/`. Uppdatera berörd dokumentation när ett arbetssätt eller beslut ändras; undvik ändringsloggar och dubblerade instruktioner.
- Använd [run-dev](.agents/skills/run-dev/SKILL.md) för att starta appen och verifiera flöden lokalt. Använd syntetiska konton och data. Andra worktrees delar maskin och Docker: välj en egen ledig portserie för en ny miljö, stoppa bara egna processer med deras PID och rör inte andras containrar, volymer eller git stash.
- Verifiera ändrat beteende på lägsta nivå som visar regeln: beräkningstester för rekordmotorn, databas-/integrationstester för lagring och åtkomst, och webbläsarkontroll för ändrade flöden. Kontrollera att privata pass inte kan läsas eller ändras av en annan användare när åtkomstlogik ändras.
- Före commit av applikationskod: kör `npm run build` i `web/` med Node-versionen i `.nvmrc` och `dotnet test --project api/Kuta.Api.Tests` mot lokal Supabase. Testerna använder standardinstansen, eller den egna instansen med `KUTA_PORTS=BAS` (samma BAS som `run-dev.sh --ports`); kör mot en egen instans i stället för att ändra eller återställa en lånad miljö. Redovisa om de inte kunnat köras. För enbart dokumentation eller instruktioner räcker granskning av diff, lokala länkar och beskrivna kommandon mot implementationen.
- Redovisa vad som verifierats, resultatet och miljön/versionen som kontrollerats. En pågående kontroll är inte godkänd. Återanvänd tidigare resultat bara om relevanta kod-, test- och konfigurationsunderlag är oförändrade.
- Ändra inte hostad databas, hemligheter eller driftsättning om uppgiften inte omfattar just den åtgärden. Verifiering sker lokalt; en instruktion om att testa innebär inte en instruktion om att deploya.

## Stack (beslutad)
- **API:** .NET 10, ASP.NET Core minimal API, **Dapper** (inte EF Core), Npgsql, Postgres + PostGIS, NetTopologySuite.
- **Supabase:** hanterad Postgres (EU-region) och **Supabase Auth**. Inloggningen går via vårt API (BFF): API:t pratar med Supabase Auth och lägger sessionen i en krypterad HttpOnly-cookie, så tokens aldrig når JavaScript. Webben använder varken supabase-js eller Supabases Data API och behöver inga Supabase-nycklar. Lokalt håller sig allt till 553xx: webben 55300 (preview 55301), API:t 55302, Supabase 55320–55329 (se `supabase/config.toml`). Starta allt med `scripts/run-dev.sh`; `--ports BAS` kör en extra instans med egen Supabase-stack (se README).
- **Migreringar:** DbUp med inbäddade SQL-filer i `api/Kuta.Api/Migrations/`.
- **Web:** React + Vite + TypeScript, TanStack Router (kodbaserad) + Query, Tailwind v4, shadcn/ui (components.json finns, komponenter läggs till med `npx shadcn@latest add ...`), Motion för animationer, vite-plugin-pwa, MapLibre GL.
- **Filer:** Cloudflare R2 (S3-kompatibelt) för rå-FIT-filer och PMTiles-kartor. `activities.raw_file_key` pekar på objektnyckeln.
- **Jobb/köer:** börja med Postgres-baserad kö (Hangfire eller Wolverine), inte Redis/RabbitMQ. E-post och web push kommer senare.
- **FIT-parsning:** Garmins officiella C#-SDK (NuGet `Garmin.FIT.Sdk`).

## Status
- `web/`: bygger (`npm run build` OK). Skal med routes Flöde/Pass/Statistik/Profil, bottennavigering, sidövergångar, PWA-manifest. PNG-ikoner (192/512/maskable, apple-touch-icon) saknas.
- `api/`: bygger och körs mot lokal Supabase; `/api/health` svarar med PostGIS-version. Paketversioner pinnade. Namnmatchning snake_case→PascalCase är satt via `DefaultTypeMap.MatchNamesWithUnderscores`.
- Auth (`SupabaseAuthClient`/`SupabaseCookieSession`):
  - `POST /api/auth/signup|login|logout`, `GET /api/auth/confirm?token_hash=` (länken i bekräftelsemejlet, mall i `supabase/templates/confirmation.html`), `GET /api/antiforgery`.
  - Alla skrivande `/api`-anrop kräver `X-CSRF-TOKEN` från `GET /api/antiforgery`. Token är knuten till användaren: hämta en ny efter in- och utloggning.
  - Sessioner i `user_sessions` (kontrolleras vid varje anrop, utloggning slår igenom direkt). Cookien glider 30 dagar, hård gräns 180 dagar. Access-token förnyas strax före utgång; Supabase tillfrågas om återkallelse högst var 5:e minut.
  - Data Protection-nycklar i `data_protection_keys`. Rate limit 10 auth-anrop/5 min/IP.
  - `users` är en profiltabell med samma id som `auth.users` (FK, kaskadradering). `GET /api/me` ger id, e-post och `displayName` (null tills profilen skapats); `PUT /api/me` skapar/uppdaterar profilen.
    - Webben: `src/lib/api.ts` (fetch med CSRF-token, `ApiError` från API:ts `{ error }`), `src/lib/auth.ts` (`meQuery`, null = utloggad), `src/routes/auth.tsx` (inloggning/registrering/kolla e-post, namnval första gången). `Layout` visar inloggningen i stället för appen när `me` är null. Fel från API:t har formen `{ "error": "..." }` på svenska och visas som de är.
  - PWA:ns service worker får aldrig svara på navigeringar till `/api/` (`navigateFallbackDenylist` i `vite.config.ts`) – annars når bekräftelselänken aldrig API:t.
  - Lösenordsåterställning: `POST /api/auth/recover`, mejllänk till `GET /api/auth/reset-password?token_hash=`, sedan `POST /api/auth/reset-password`. Separat krypterad HttpOnly-cookie (högst 15 minuter), engångsanvändning via `user_sessions`; ger ingen appåtkomst. Lyckat byte avslutar kontots Kuta-sessioner. Mall och driftinställning finns i README.
  - Saknas: MFA, inloggning med Google/Apple, logga ut andra sessioner, städning av gamla sessionsrader, Turnstile.
  - Lokalt: kommer bekräftelsemejlet i Mailpit (http://127.0.0.1:55324) med Supabases engelska standardmall har auth-containern startat före Kong – kör `docker restart supabase_auth_kuta`.
- FIT-uppladdning (`ActivityEndpoints`, `FitImport`, `RawFileStore`):
  - `POST /api/activities` (multipart, fältet `file`, högst 20 MB) tolkar filen med `Garmin.FIT.Sdk` (första sessionen + records) och skriver `activities` + `activity_streams` i en transaktion; rutten förenklas med Douglas-Peucker (~5 m). Samma fil igen ger 409 (`raw_file_sha256` unik per användare).
  - **Pulssamtycke:** `PUT`/`DELETE /api/me/hr-consent`, `users.hr_consent_at`, `hrConsent` i `/api/me`. Utan samtycke sparas varken puls eller råfil (råfilen innehåller puls). Återkallat samtycke raderar all sparad puls och alla råfiler. Ett pass uppladdat utan samtycke får puls bara om det laddas upp igen (hashen gör det till en dubblett – ta bort passet först när borttagning finns).
  - Råfiler via S3-protokollet (`Storage__ServiceUrl/Region/Bucket/AccessKeyId/SecretAccessKey`): lokalt Supabase Storage (bucket `fit-files` i `supabase/config.toml`), i drift R2 eller Supabase Storage.
  - Webben: passsidan (`routes/activities.tsx`) har uppladdning, pulssamtycke och passlista (km, tid, tempo).
  - Saknas: passsida med karta/grafer, bästa insatser (rekordmotorn), radering av pass och av råfiler vid kontoradering, multisport (flera sessioner), GPX.
- Ändra aldrig en körd migrering – nya ändringar i nästa nummer. Migreringsfilerna finns i `api/Kuta.Api/Migrations/`.
- Privat repo: github.com/simonauner/kuta. Node 24 (`.nvmrc`).
- Driftsatt på https://kuta.fly.dev (en maskin i `arn`, `fly deploy --ha=false` tills migreringar och token-förnyelse klarar flera maskiner; ingen automatisk deploy vid push): `Dockerfile` (API:t servar den byggda webben från `wwwroot`, samma origin), `fly.toml` (egen app `kuta`, `arn`), steg i README. Utanför Development krävs `DataProtection__Certificate`; lokala värden ligger bara i `appsettings.Development.json`. Pipelinens ordning i `Program.cs` är medveten (headers → statiska filer → routing → auth) – se kommentarerna.
- Hostat Supabase-projekt: `https://hrkqzqfyisvdyzfxzokf.supabase.co`. Data Protection-nycklarna i drift är verifierat krypterade med certifikatet. Egen SMTP saknas (Scaleway TEM, kräver verifierad domän; domänen blir kutaapp.com) – tills dess når bekräftelsemejl bara projektets medlemmar.
- **Hemligheter bara via 1Password:** `fly.secrets.tpl` (op://-referenser, objektet `fly-kuta` i valvet `Development`) och `scripts/fly-secrets.sh fly.toml --stage`. Aldrig värden i filer, argument eller kommandon; icke-hemliga inställningar i `fly.toml` under `[env]`. Vid uppsättning: EU-region, stäng av Data API i dashboarden (grants är redan återkallade, se nedan), lägg in bekräftelsemallen och minsta lösenordslängd 8 i dashboarden, och sätt via miljövariabler: `ConnectionStrings__Default` (session pooler, port 5432, `Maximum Pool Size` under poolstorleken), `Supabase__Url`, `Supabase__PublishableKey` och `Supabase__SecretKey` (gör att besökarens IP skickas vidare till Supabase). Före produktion: skydda Data Protection-nycklarna med certifikat och konfigurera forwarded headers så att rate limiten ser klientens IP.

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

## Säkerhet (XSS/CSRF)
- **CSRF:** sessionscookien är `SameSite=Lax` och alla skrivande `/api`-anrop kräver `X-CSRF-TOKEN`. GET-anrop får aldrig ändra data (undantagen är mejllänkarna `/api/auth/confirm` och `/api/auth/reset-password`, som verifierar engångstoken och skapar en session; själva lösenordsbytet kräver POST). Öppna inte CORS.
- **XSS:** tokens ligger i HttpOnly-cookien och går inte att stjäla, men ett injicerat skript kan ändå anropa API:t som användaren. Förhindra XSS: rendera användardata som text i React, aldrig `dangerouslySetInnerHTML` med användardata, inga `javascript:`-länkar från användardata.
- **CSP för webben** injiceras vid bygget i `web/vite.config.ts` (`script-src 'self'`, inga inline-skript eller `<style>`-taggar). Lägg till origins där när appen hämtar från nya ställen (t.ex. R2 för kartor) i stället för att lätta på reglerna. Kolla konsolen efter CSP-fel med `npm run build && npx vite preview` när beroenden läggs till – bibliotek som injicerar `<style>` behöver hanteras.
- **API-svar** har `default-src 'none'; frame-ancestors 'none'`, `nosniff` och `X-Frame-Options: DENY` (middleware i `Program.cs`, test i `SecurityHeadersTests`).
- **Där webben hostas** sätts som headers (går inte i meta-taggen): `Content-Security-Policy: frame-ancestors 'none'`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` och HSTS. Servera webben och API:t från samma origin.

## Nästa steg (i ordning)
1. ~~GitHub-repo~~, ~~bygga och köra API:t~~, ~~auth (API + webb)~~, ~~driftsättning (Fly + Supabase)~~ – klart.
2. Egen SMTP (Scaleway TEM) och egen domän **kutaapp.com** (kuta.io/.se/.com är tagna): verifiera domänen, SMTP i Supabase, Site URL och certifikat hos Fly.
3. ~~Lösenordsåterställning (Supabase `/recover` + mejlmall som länkar till API:t)~~ – implementerad; återställningsmallen måste konfigureras i hostad Supabase före användning i drift.
4. ~~FIT-uppladdning~~ (se Status). Före deploy: lagring i drift (R2 eller Supabase Storage) och `Storage__*`.
5. Rekordmotor (glidande fönster över distans/tid-strömmen), tester med riktiga FIT-filer.
6. Webb: uppladdning, passsida med karta + grafer (Recharts, uPlot för långa serier), statistikvyer, skor.
7. Därefter: följare, flöde, grupper; e-post och push.

## Konventioner
- Svenska i UI-texter, engelska i kod och commit-meddelanden.
- JSON i API:t är lowerCamelCase. Returnera typade records från Dapper, aldrig `dynamic`-rader (de serialiseras med snake_case-kolumnnamn som nycklar).
- Små, fokuserade commits. Följ verifieringen under Arbetsflöde innan commit.
