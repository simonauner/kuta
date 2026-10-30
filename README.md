# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Publik startsida och app

`/` är en statisk, svenskspråkig startsida (`web/index.html`, stilar i `web/src/landing.css`). Fotot i `web/public/images/forest-trail.webp` tillhör Simon Auner och serveras lokalt som WebP, utan beroende av Stravas bildserver; produktillustrationerna är designskisser med syntetiska exempeldata. Startsidan och appen delar färgvariabler i `web/src/theme.css` och följer systemets ljusa eller mörka tema med neutrala ytor och orange accent. Innehåll och SEO-metadata finns i HTML utan JavaScript. `robots.txt` och `sitemap.xml` finns i `web/public/`; canonical och sitemap använder `https://kutaapp.com/`.

Logotypen ”Spåret” finns som fristående SVG i `web/public/brand/` (mörk och ljus variant). Bokstäverna är vektorkonturer och kräver ingen font. Symbolen i `web/public/favicon.svg` används också av PWA-manifestet. PNG-ikoner för PWA finns i `web/public/icons/` (192/512 samt maskable 512 med extra marginal). `web/public/apple-touch-icon.png` (180×180, heltäckande bakgrund) används av iOS, och `favicon-32.png` är PNG-fallback för webbläsare. Båda HTML-dokumenten länkar till ikonerna; PWA:n startar på `/app`. SVG-underlagen för hemskärmsikonerna finns i `web/public/brand/app-icon.svg` och `app-icon-maskable.svg`.

React-appen öppnas på `/app`, med vyerna `/app/activities`, `/app/stats` och `/app/profile`. `web/app.html` har `noindex`. Mejlets bekräftelselänk återvänder till `/app`. Vite bygger båda HTML-filerna, och .NET serverar dem från samma origin. PWA:n startar på `/app` och använder bara appskalet som navigeringsfallback inom `/app`.

## Språk och URL:er

React-appen använder `react-i18next` med svenska och engelska. Språkväljaren finns både vid inloggning och i appen. Valet sparas lokalt i webbläsaren (`kuta.language`); utan sparat val prioriteras svenskt IP, sedan det första stödda språket i webbläsarens språklista, annars engelska. Bara uttryckliga val sparas. API:t bedömer svenska IPv4-/IPv6-nät med en lokal lista, utan externa IP-uppslag; källan och hur listan uppdateras finns i [Localization/README.md](api/Kuta.Api/Localization/README.md). Om uppslaget misslyckas eller tar mer än 1,5 sekunder används webbläsarens språk. Valet uppdaterar även HTML-dokumentets `lang`. Det synkas inte mellan enheter.

Appens URL:er är `/app` (flöde), `/app/activities`, `/app/stats` och `/app/profile` på båda språken.

Översättningar finns i `web/src/locales/sv.json` och `en.json`; lägg till samma nycklar i båda. API-felkoder översätts i webben, med ett generellt fel för okända koder. Mejlmallar och PWA-manifest är tills vidare svenska. Språkval och katalogernas täckning kontrolleras med `cd web && npm test` (Node enligt `.nvmrc`).

## Köra lokalt

Kräver Docker, Supabase CLI, .NET SDK och nvm (Node-versionen står i `.nvmrc`). `global.json` väljer .NET SDK 10.0.402 eller senare stabil patch i 10.0.4xx-serien, både lokalt och i Dockerbygget. Allt startas med:

```bash
scripts/run-dev.sh
```

Skriptet startar Supabase om det inte redan kör, bygger och startar API:t och kör webben med Vite (hot reload), med loggarna från båda i samma terminal. Ctrl+C stoppar API:t och webben; Supabase fortsätter köra tills `supabase stop`. API:t laddas inte om vid ändringar – starta om skriptet. Allt lokalt ligger på portar i 553xx-serien.

### Flera instanser samtidigt

```bash
scripts/run-dev.sh --ports 55400
```

Flyttar hela portserien: webben på BAS, API:t på BAS+2 och en egen Supabase-stack med egen databas på BAS+20–29 (Mailpit BAS+24). Konfigurationen och bygget hamnar i `.context/` (ignoreras av Git). Stoppa den instansens Supabase med `supabase stop --workdir .context/supabase-55400`. Kör testerna mot den med `KUTA_PORTS=55400 dotnet test --project api/Kuta.Api.Tests`.

### Adresser (standardinstansen)

| Vad | Adress |
|---|---|
| Webben | http://localhost:55300 |
| API | http://localhost:55302 |
| Mailpit (bekräftelsemejl) | http://127.0.0.1:55324 |
| Supabase Studio | http://127.0.0.1:55323 |

Kommer bekräftelsemejlet ändå på engelska med en länk till Supabase har auth-containern startat före Kong (skriptet hanterar det när det själv startar Supabase): `docker restart supabase_auth_kuta` och registrera igen.

### Lösenordsåterställning

Välj **Glömt lösenord?** på inloggningssidan. Mejlet finns i instansens Mailpit och länkar till `/api/auth/reset-password`. API:t verifierar länken och ger en separat krypterad HttpOnly-cookie som gäller i högst 15 minuter och bara för lösenordsbytet. Efter bytet avslutas kontots Kuta-sessioner och användaren loggar in med det nya lösenordet. Om anslutningen bryts under lösenordsbytet avslutas sessionerna också, eftersom bytet kan ha hunnit lyckas hos Supabase. Begär då en ny återställningslänk vid behov. Både begäran och lösenordsbytet kräver CSRF-token; ogiltiga, använda och utgångna länkar kräver en ny begäran.

Den lokala mejlmallen anges i `supabase/config.toml`. Om en redan körande miljö saknar återställningsmallen måste dess ägare stoppa och starta just den Supabase-instansen för att läsa in konfigurationen. Ändra inte en lånad instans.

Tester (kräver `supabase start`, med Storage påslaget): `dotnet test --project api/Kuta.Api.Tests`, eller `KUTA_PORTS=BAS dotnet test …` mot en egen instans.

Frontendkontroller med Node-versionen i `.nvmrc`: kör `npm ci` i `web/`, sedan
`npm run format:check`, `npm run lint`, `npm test` och `npm run build`.
`npm run format` formaterar webbfilerna med Prettier. API-klientens tester använder
Nodes inbyggda testverktyg och mockade nätverksanrop; de kräver inte Supabase.
GitHub Actions kör frontendkontrollerna på berörda pull requests och push till `main`.

API-jobbet i `.github/workflows/api.yml` kör hela `api/Kuta.Api.Tests` vid ändringar
i API:t, Supabase-konfigurationen, `global.json`, `Dockerfile` eller själva jobbet,
både på pull requests och push till `main`. Det installerar .NET enligt `global.json`
och Supabase CLI 2.72.7, startar en tillfällig lokal Supabase med Auth, Mailpit och
Storage, läser dess lokala nycklar och väntar på auth efter omstart innan testerna
körs. Testerna körs sekventiellt (`-- --parallel none`) så att testservrarna inte
försöker migrera den tomma databasen samtidigt. Inga produktionshemligheter
eller hostade tjänster används. Supabase stoppas även om testerna misslyckas.

Footern visar copyright och webbens byggnummer (`ÅÅÅÅMMDD.ttmmss`, UTC), som sätts
automatiskt av Vite vid bygget, även i Docker. I utvecklingsläge visas `Version utveckling`.

## Bästa insatser och rekord

FIT-importen räknar bästa insats för löpning över 1 km, 1 mile, 5 km, 10 km,
halvmaraton och maraton och sparar dem i samma transaktion som passet. Beräkningen
använder tids- och distansströmmen med linjär interpolering mellan mätpunkterna.
Tiden är förfluten tid inklusive pauser, inte rörelsetid. Ingen distans extrapoleras
före första eller efter sista mätpunkten. Saknade/ogiltiga värden, återställd distans
och icke stigande tid bryter strömmen; ett rekord får inte korsa en sådan brytpunkt.
Utan en sammanhängande ström över hela distansen blir det ingen insats.

`distanceM` är standarddistansens heltalsetikett: 1000, 1609, 5000, 10000, 21097
eller 42195. Motorn använder exakt 1609,344 m för milen och 21097,5 m för halvmaran.
`elapsedS` och `startOffsetS` är sekunder med decimaler. `achievedAt` är insatsens
starttid i UTC (passets starttid plus `startOffsetS`).

Alla anrop kräver inloggning och ger bara den egna användarens data:

| Anrop | Resultat |
|---|---|
| `GET /api/activities/{id}/best-efforts` | Passets bästa insatser, sorterade på distans; 404 för okänt eller annans pass. |
| `GET /api/records` | Snabbaste insatsen per distans; vid lika tid väljs den tidigaste. |
| `GET /api/records?year=2026` | Samma urval inom UTC-kalenderåret då insatsen började. |
| `POST /api/activities/{id}/best-efforts/recalculate` | Ersätter passets insatser från sparade tidsserier; 204 vid framgång, annars 404 för okänt eller annans pass. Kräver `X-CSRF-TOKEN`. |

Omberäkningen kan upprepas efter ändrad beräkningslogik och fungerar även utan
sparad råfil eller pulssamtycke. Kör den för befintliga pass som importerats innan
rekordmotorn infördes; de fylls inte automatiskt i vid migreringen. Pass utan
distansström och andra sporter ger en tom lista. Person- och årsbästa beräknas från
`best_efforts`, så borttagning av ett pass gör att nästa bästa insats tar över direkt.

Beräkningstesterna jämför också med en uttömmande referensberäkning på oregelbundna
strömmar. Integrationstesterna använder syntetiska FIT-filer skapade med Garmins SDK
och kontrollerar import, omberäkning, årsskifte, kaskadradering, CSRF och ägarskap.

## Driftsättning (Fly.io)

En egen Fly-app (`kuta`, region `arn`). `Dockerfile` bygger webben och API:t till en image där API:t servar webben, så att de delar origin. Migreringarna körs när appen startar.

**Domän före deploy:** `CanonicalHost` i `fly.toml` är `kutaapp.com`. Konfigurera DNS för domänen mot Fly-appen och verifiera att Fly har ett giltigt TLS-certifikat för `kutaapp.com` innan du deployar denna konfiguration. Appen skickar vidare `kuta.fly.dev` och `www.kutaapp.com` till `https://kutaapp.com`.

**Supabase-projektet** (en gång, i dashboarden):
- EU-region. Stäng av Data API. Minsta lösenordslängd 8.
- Authentication → URL Configuration: Site URL `https://kutaapp.com` (bekräftelselänken byggs på den). Adressen ska följa `CanonicalHost` i `fly.toml` och vara inställd före deploy.
- Authentication → Email Templates → Confirm signup: innehållet i `supabase/templates/confirmation.html`.
- Authentication → Email Templates → Reset password: innehållet i `supabase/templates/recovery.html`. Krävs före användning av lösenordsåterställning i drift; standardmallen skickar sessionstokens till webbläsaren i stället för vårt API.
- Egen SMTP (Authentication → Emails → SMTP Settings): Scaleway TEM, host `smtp.tem.scaleway.com`, port 587, användare = Scaleway-projektets id, lösenord = secret key för en API-nyckel med bara TEM-behörighet (sparad i 1Password), avsändare på kutaapp.com. Domänen verifieras i Scaleway med SPF, DKIM och DMARC hos Hostup. Höj mejlgränsen under Authentication → Rate Limits. Supabases inbyggda mejl skickar bara till projektets egna medlemmar.

**Hemligheter** ligger i 1Password, aldrig i filer eller kommandon. [fly.secrets.tpl](fly.secrets.tpl) listar dem som `op://`-referenser; värdena finns i objektet `fly-kuta` i valvet `Development`, ett fält per hemlighet med hemlighetens namn:

| Fält | Värde |
|---|---|
| `ConnectionStrings__Default` | Supabase → Connect → Session pooler, i Npgsql-format: `Host=…;Port=5432;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Maximum Pool Size=10` |
| `Supabase__SecretKey` | Supabase → Project Settings → API Keys → secret key (`sb_secret_…`) |
| `DataProtection__Certificate` | Certifikatet som krypterar Data Protection-nycklarna, base64 på en rad (se nedan) |
| `Storage__AccessKeyId`, `Storage__SecretAccessKey` | S3-nyckel för bucketen med råa FIT-filer (se nedan) |

**Lagring för råa FIT-filer:** en privat bucket som nås via S3-protokollet, i R2 (Cloudflare) eller Supabase Storage (Storage → S3 Connection). Lägg `Storage__ServiceUrl`, `Storage__Region` och `Storage__Bucket` i `fly.toml` under `[env]` och nyckeln i 1Password enligt tabellen. Utan dessa går appen att starta, men uppladdning och återkallat pulssamtycke ger fel.

Certifikatet skapas i en tillfällig katalog utanför repot och kopieras till urklipp; klistra in det i fältet, och töm sedan urklippet:

```bash
(cd "$(mktemp -d)" && openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 3650 -subj "/CN=kuta-dataprotection" -keyout key.pem -out cert.pem 2>/dev/null && openssl pkcs12 -export -inkey key.pem -in cert.pem -passout pass: | base64 | tr -d '\n' | pbcopy && rm -f key.pem cert.pem)
```

```bash
pbcopy < /dev/null
```

Förlorat certifikat förlorar ingen data, men alla måste logga in igen. Data Protection kontrollerar inte certifikatets utgångsdatum.

**Importera hemligheterna** (1Password CLI inloggad; värdena går från `op inject` till `fly secrets import` i minnet, och ingenting importeras om en referens saknas). Kör igen efter varje ändring i 1Password; en ny hemlighet läggs till i mallen och som fält samtidigt.

```bash
scripts/fly-secrets.sh fly.toml --stage
```

**Deploy:** kör en maskin tills migreringar och token-förnyelse klarar flera maskiner, enligt `AGENTS.md`.

```bash
fly deploy --ha=false
```
