# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Publik startsida och app

`/` är en statisk, svenskspråkig startsida (`web/index.html`, stilar i `web/src/landing.css`). Fotot i `web/public/images/forest-trail.webp` tillhör Simon Auner och serveras lokalt som WebP, utan beroende av Stravas bildserver; produktbilderna är riktiga appskärmdumpar, med syntetiska exempeldata utom Simons egna pass (Åre Fjällmaraton och Västerås swimrun, med datum, beskrivning, klocka och utrustning bortklippta). Startsidan positionerar Kuta som en komplett löparapp utan betalvägg: flytt med Strava-arkiv och FIT-filer, gratis analys, mål, att följa vänner och data i Sverige. Garmin nämns inte förrän Garmin godkänt det, och supporterskapet först när betalningen finns. Den visar Åre-passet i mobilram, en pågående Strava-import (`strava-import.webp`, syntetiskt jobb i lokal databas), löprekord och träningsmål i mobilen samt profilen med aktivitetskalender (`profile-mobile.webp`, också sektion 05 på funktionssidan). `/features` är en statisk svensk funktionssida (`web/features.html`, `web/src/features.css`), länkad från startsidans funktionssektion. Bilderna i `web/public/images/features/` är lokala skärmdumpar i mörkt tema, sparade som WebP (`cwebp -q 85 -m 6`); bara `statistics.png` är kvar som PNG för `og:image`. Sektionerna är import (bildspel med FIT-uppladdning och Strava-import), utveckling, multisport, mål, profil och sist skor. Statistiksektionen visar passdetaljer med karta, löprekord, utveckling, prognos och årsöversikt i ett bildspel (`web/src/features.ts`, som driver alla `.statistics-carousel` på sidan); mobilbilderna är tagna vid mobilbredd i 3× upplösning (1170 × 2280 px) och visas i en CSS-telefonram med appens riktiga bottennavigering. Bildspelet kan pausas och styras manuellt, växlar bara när det syns och startar utan automatisk växling vid minskad rörelse. Utan JavaScript visas första bilden. Multisportsektionen visar uppdelningsverktyget för Västerås swimrun i samma telefonram. Träningsmål har en egen sektion med två mobilbilder i samma telefonram: skapa mål-modalen och Pass-sidan med ett pågående mål, ett uppnått mål och två veckors streak. Båda visar syntetiska data. Startsidan och funktionssidan delar bildstilar och telefonramar i `web/src/product-shots.css`, som importeras av `landing.css`. FIT-uppladdningen (1347 × 1080 px) är en desktopbild i 3× upplösning; utrustning visas i samma mobilformat som de andra telefonbilderna. De publika sidorna och appen delar systemtypsnitt och färgvariabler i `web/src/theme.css`; publika knappar följer appens storlek och hörnradie, och sidfotslänkarna staplas till höger. `Footer` i `web/src/components/footer.tsx` och dess CSS delas av alla sidor: Vite renderar varianten `public` till statisk HTML, medan appens `Layout` använder varianten `app` med valt språk och version. Sidorna följer systemets ljusa eller mörka tema med neutrala ytor och orange accent. Innehåll och SEO-metadata finns i HTML utan JavaScript. Startsidan och funktionssidan finns även på engelska som `/en` och `/en/features` (`web/en/`), med flagglänk till det andra språket i sidhuvudet (`web/public/flags/`) och `hreflang`-alternativ; håll båda språkversionerna i takt när innehållet ändras. `web/src/public-language.ts` skickar besökaren till rätt språkversion med appens prioritet (sparat val → svensk IP via `/api/locale` → webbläsarens språk → engelska); IP-uppslaget görs bara när webbläsaren inte redan ger svenska. Ett klick på flaggan sparas som uttryckligt val i `kuta.language`, samma nyckel som appen. Sökrobotar (user agent) skickas inte vidare, så att båda versionerna kan indexeras. Sidfoten i de engelska sidorna får engelska etiketter och länkar till policyernas engelska delar. `robots.txt` och `sitemap.xml` finns i `web/public/`; canonical och sitemap använder `https://kutaapp.com/`.

Logotypen ”Spåret” finns som fristående SVG i `web/public/brand/` (mörk och ljus variant). Bokstäverna är vektorkonturer och kräver ingen font. Symbolen i `web/public/favicon.svg` används också av PWA-manifestet. PNG-ikoner för PWA finns i `web/public/icons/` (192/512 samt maskable 512 med extra marginal). `web/public/apple-touch-icon.png` (180×180, heltäckande bakgrund) används av iOS, och `favicon-32.png` är PNG-fallback för webbläsare. Båda HTML-dokumenten länkar till ikonerna; PWA:n startar på `/app`. SVG-underlagen för hemskärmsikonerna finns i `web/public/brand/app-icon.svg` och `app-icon-maskable.svg`.

`/privacy` är en publik, statisk integritetspolicy (`web/privacy.html`, extra stilar i `web/src/privacy.css`) med svenska och engelska i samma dokument. Engelska kan länkas direkt med `/privacy#english`. Båda språken är läsbara utan JavaScript; React-appens länkar använder dess valda språk. Startsidan och inloggningssidan länkar till policyn. `/terms` (`web/terms.html`) är användarvillkoren i samma form, med anmälan om missbruk till privacy@kutaapp.com. Registreringen kräver att båda godkänns (`acceptTerms: true` i `POST /api/auth/signup`, annars 400 `termsRequired`); version (`AuthEndpoints.TermsVersion`, samma datum som på villkorssidan) och tidpunkt skickas som Supabase-metadata och kopieras till `users.terms_version`/`terms_accepted_at` (migrering 022) när profilen skapas. Ändras villkoren: uppdatera datumet på sidan och konstanten tillsammans. Nytt godkännande för befintliga användare är inte byggt än.

Policyn är ett utkast inför Garminansökan. Verifierat underlag, beslut, förslag och kvarvarande kontroller finns under [Integritet: underlag och öppna frågor](#integritet-underlag-och-öppna-frågor). Uppdatera båda språkversionerna när uppgifterna ändras; Garminanslutningen beskrivs som planerad tills den är implementerad.

React-appen öppnas på `/app`, med vyerna `/app/activities`, `/app/stats` samt `/app/profile` och `/app/settings`. `web/app.html` har `noindex`. Mejlets bekräftelselänk bekräftar adressen och återvänder till inloggningen på `/app`; den loggar inte in, så att en länk till någon annans konto inte kan logga in den som klickar. Vite bygger de tre HTML-filerna, och .NET serverar dem från samma origin. PWA:n startar på `/app` och använder bara appskalet som navigeringsfallback inom `/app`.

Profil visar löparens namn och egna pass med samma passkort och kartminiatyrer som andra löparprofiler, inklusive privata pass. Kugghjulet öppnar Inställningar på `/app/settings`, med länkar till Följare på `/app/settings/followers` och Utrustning på `/app/settings/equipment`, språkval, dolda platser, pulssamtycke, Strava-import, dataexport, e-postadress, utloggning och Radera data för att radera alla pass eller kontot. Undersidorna visar hela hierarkin i brödsmulorna: Profil → Inställningar → Följare respektive Profil → Inställningar → Utrustning. Vanlig FIT-uppladdning och manuell registrering nås via Aktiviteter → Ny aktivitet. Pulssamtycket ändras under Inställningar; skapa-sidan visar samma checkbox tills samtycket är givet. Checkboxen visas även när samtycket redan är givet.

## Språk och URL:er

React-appen använder `react-i18next` med svenska och engelska. Språkväljaren finns vid inloggning och under Inställningar i appen. Valet sparas lokalt i webbläsaren (`kuta.language`); utan sparat val prioriteras svenskt IP, sedan det första stödda språket i webbläsarens språklista, annars engelska. Bara uttryckliga val sparas. API:t bedömer svenska IPv4-/IPv6-nät med en lokal lista, utan externa IP-uppslag; källan och hur listan uppdateras finns i [Localization/README.md](api/Kuta.Api/Localization/README.md). Om uppslaget misslyckas eller tar mer än 1,5 sekunder används webbläsarens språk. Valet uppdaterar även HTML-dokumentets `lang`. Det synkas inte mellan enheter.

Appens URL:er är `/app` (flöde), `/app/activities`, `/app/stats`, `/app/profile` och `/app/settings` på båda språken.

Översättningar finns i `web/src/locales/sv.json` och `en.json`; lägg till samma nycklar i båda. API-felkoder översätts i webben, med ett generellt fel för okända koder. Mejlmallar och PWA-manifest är tills vidare svenska. Språkval och katalogernas täckning kontrolleras med `cd web && npm test` (Node enligt `.nvmrc`).

## Följare och flöde

När flödet är tomt visas genvägar till ett första pass, en guide till Import och anslutningar (koppla Garmin, importera från Strava) och kopiering av den egna profillänken. Den som redan följer någon får en text om att inga pass har delats ännu. Välkomstvyn försvinner när flödet får innehåll. Aktiviteter, Statistik och den egna profilen har ett gemensamt, kort tomläge med en rad om vad som visas där när användaren saknar pass. Bara flödet har en välkomstvy; båda använder samma knappar till nytt pass och Strava-import. Tomma filterresultat och andra löpares profiler visar fortsatt sina vanliga tomlägen.

På breda skärmar (från `lg`) har flödet en sammanfattningskolumn till vänster (`components/feed-summary.tsx`): namn, antal följda, följare och aktiviteter samt veckans dagar med veckosvit, från samma `GET /api/runners/{id}/summary` som profilen. Därunder visas löpbelastning per rullande 7 dagar för de fyra senaste sjudagarsperioderna fram till i dag ur `GET /api/training-load`, där de senaste 7 dagarna jämförs med snittet av de tre perioderna före (mer än 30 % över eller under ger Mer än vanligt respektive Lugnare än vanligt, annars Jämn nivå; ingen jämförelse med kortare historik än 28 dagar; `rollingWeekLoads` och `weekTrend` i `lib/training-load.ts`). Belastningsdelen döljs utan tröskeltempo eller löpning och tonas in om den laddas efter kortet. På smala skärmar visas inte kolumnen; samma siffror finns på Profil.

Flödet på `/app` visar egna pass och pass från löpare du följer efter deras godkännande,
nyaste först, 25 åt gången. **Inställningar → Följare** innehåller inkommande förfrågningar
och listor över följare/följda. Det finns ingen användarkatalog eller namnsökning, varken i
webben eller API:t. **Dela profil** på den egna profilen kopierar en kort länk
`/app/r/{kod}` på aktuell origin, med bekräftelse och ett manuellt kopieringsfält om
webbläsaren nekar clipboard-åtkomst. Mottagaren loggar in, koden slås upp och webben går vidare
till `/app/runners/{id}`, där en följförfrågan kan skickas. Länken ändrar inte passens synlighet
eller följgodkännandet.

Koden (`users.share_code`, migrering 028, `ShareCodes.cs`) är 8 tecken ur `a–z` och `2–9` utan
förväxlingsbara tecken (0/o, 1/l/i), slumpas med kryptografisk RNG och skapas först när den behövs:
`POST /api/me/share-code` ger egen kod (skapar den vid behov; POST eftersom GET aldrig ändrar data),
`POST /api/me/share-code/replace` ersätter den och `GET /api/share-codes/{kod}` ger `{ runnerId }`
eller 404. Uppslaget kräver inloggning och har rate limiten `shareCodes`, 30 anrop/timme/användare,
så att koder inte kan gissas fram. **Inställningar → Profil och synlighet → Profillänk → Skapa ny länk** byter kod efter en
bekräftelse; den gamla länken slutar då fungera. Den som redan hittat profilen (flöde, följlistor,
`/app/runners/{id}`) kommer fortfarande åt den: koden hittar bara profilen, och blockering och
synlighet styr läsningen. Koden ingår i `profile.json` i exporten.
Godkänn eller avböj förfrågningar, avbryt egna förfrågningar, sluta följa eller ta bort följare.
Relationerna är riktade: ett godkännande innebär inte att båda automatiskt följer varandra.
Namn i flödet och följarlistorna länkar till `/app/runners/{id}`. Profilsidan visar löparens
namn, följstatus och de pass besökaren har läsåtkomst till, 25 åt gången. Där kan du också
sluta följa, avbryta en förfrågan eller skicka en ny. Efter avföljning försvinner följarpassen;
offentliga pass finns kvar på profilen. Egna privata pass visas bara på den egna profilen.

Nya pass är privata. På **Redigera aktivitet → Synlighet** kan ägaren välja `private`
(bara ägaren), `followers` (godkända följare) eller `public` (alla inloggade på Kuta med
länken). Flödet är inget globalt upptäcktsflöde: offentliga pass från andra än dem du följer
visas inte där. Privata pass blir aldrig synliga genom att en följförfrågan godkänns.
Nekad läsning eller ändring ger 404. Bara ägaren kan redigera, dela upp, räkna om rekord
eller radera pass. Befintliga listor, årsstatistik, utrustning och personliga rekord är egna.

Delade pass visar beskrivning, taggar, sportdelar, träningsdata och passets bästa insatser.
Puls (även tidsserier och varv), fullständiga GPS-tidsserier, klockmodell och utrustning
skickas bara till ägaren. Höjd inom 600 m från start och mål utelämnas också för andra, så att
höjdprofilen inte kan matchas mot en höjdmodell för att hitta startpunkten. Flödets och profilens passkort visar kartminiatyrer från den sparade
förenklade rutten. För andra än ägaren klipper PostGIS bort minst 500 meter runt originalets
start- och slutpunkt samt ägarens sparade dolda platser med respektive radie, även vid återbesök; förenkling sker före klippning och separata delar
förblir separata. Helt dolda eller saknade rutter ger ingen karta. Ägaren ser hela rutten.
Kartorna använder samma egna kartfil som passsidan (se Karta) och laddas bara nära skärmen;
rullning och beröring styr sidan, inte miniatyrkartan. Titel och beskrivning är
användartext; användaren behöver själv välja vad den delar där.

API (alla kräver inloggning; skrivningar kräver CSRF):

- `GET /api/runners/{id}` ger `{ id, displayName, status }`; `status` är `none`, `pending`
  eller `accepted` för den egna utgående relationen. Okänd löpare ger 404.
  `GET /api/runners` (även med `?q=`) ger 404; användare kan inte listas eller sökas.
- `GET /api/runners/{id}/activities` ger `{ items, nextCursor }` för den löparens läsbara
  pass med samma sidmarkör och skyddade kartminiatyrer som flödet. Inga privata pass från
  andra, puls eller GPS-tidsserier ingår.
- Öppen profil (`users.public_profile`, migrering 029, förval av): `PUT /api/me/public-profile` med `{ "enabled": true }` och `publicProfile` i `/api/me`. Utan konto slår `GET /api/public/share-codes/{kod}` upp en kort profillänk (samma svar som det inloggade uppslaget; profilen visas ändå bara om den är öppen), och `GET /api/public/runners/{id}`, `/api/public/runners/{id}/activities` och `/api/public/activities/{id}` samma svar som för en inloggad som inte följer löparen, men bara för pass med `public` och bara när löparen slagit på öppen profil; annars 404, även för okänd löpare. Besökaren räknas som ingen användare (`Guid.Empty`) i läsregeln, så kartor maskeras och puls, GPS-tidsserier, klocka och utrustning döljs som för andra. Mål visas inte. Högst 120 anrop/minut/IP. Webben visar profil- och passlänkar för utloggade i ett eget skal med Logga in/Skapa konto, och en uppmaning att logga in när profilen eller passet inte är öppet.
- `GET /api/follows` ger `{ following, followers, blocked }`, inklusive väntande förfrågningar. Väntande förfrågningar har `joinedAt` (när kontot skapades), eftersom namn är fria och kan imitera någon, och `similarTo` när namnet liknar en befintlig kontakts (`MeEndpoints.NameSkeleton`: skiftläge, accenter, blanksteg, emoji och vanliga kyrilliska/grekiska förväxlingar). Alla löpare har `previousName` i 30 dagar efter ett namnbyte (migrering 027); namnet kan bytas högst en gång i veckan (`409 nameChangedRecently`), utom första rättelsen.
- `POST /api/following/{id}` skickar en förfrågan, idempotent även efter godkännande. Högst 20 per timme och användare. En förfrågan till någon som blockerat avsändaren sparas men visas inte för mottagaren och kan inte godkännas; avsändaren ser den som väntande, så blockeringen avslöjas inte. Förfrågningar under blockeringen tas bort när den hävs.
- `PUT`/`DELETE /api/blocks/{id}` blockerar respektive häver blockering (`follow_blocks`, migrering 024). Blockering tar bort personens följning eller förfrågan, och den blockerade kan inte läsa något delat av den som blockerat: inga pass (inte heller publika), mål eller supporterbadge (`SocialEndpoints.NotBlocked` i läsregeln).
- `PUT /api/followers/{id}` godkänner en inkommande förfrågan.
- `DELETE /api/following/{id}` avbryter en förfrågan eller slutar följa.
- `DELETE /api/followers/{id}` avböjer eller tar bort en följare. Borttagning återkallar
  läsåtkomst till följarpass vid nästa anrop; redan hämtade kopior kan inte återkallas.
- `GET /api/feed` ger `{ items, nextCursor }`. Nästa sida använder `beforeStartedAt` och
  `beforeId`, samma stabila markör som passlistan. Inga tidsserier eller puls ingår.
  `routeGeoJson` innehåller en förenklad GeoJSON-sträng
  (LineString/MultiLineString) för kartminiatyren, eller null. Delade rutter är klippta enligt
  start-/slutzonerna ovan; egna rutter är fullständiga.
- `PUT /api/activities/{id}` tar även `privacy`; utelämnat fält behåller synligheten.
- `PUT /api/activities/{id}` tar även `sport` (samma värden som vid skapande); utelämnat fält behåller sporten. Ett byte räknar om rekord och träningsbelastning i samma transaktion. Pass med sportdelar byter sport via uppdelningen och ger 400 för `sport`.
  Passdetaljer innehåller även `userId`, `displayName` och `privacy`.

Migrering 014 skapar `follows` med unik riktad relation, förbud mot självföljning,
kaskadradering och index för inkommande förfrågningar. RLS är aktiverat och Data API-grants
förblir återkallade; BFF:ens SQL äger åtkomstkontrollerna. Läsregeln delas av flöde,
passdetaljer och passets rekord. API-svar har `Cache-Control: no-store` för att undvika
återanvändning efter ändrad åtkomst. Åtkomstmatris och flödets SQL-sidindelning testas direkt
i lokal Postgres med två användare. HTTP-tester täcker kopplingen för följförfrågan →
godkännande → delat pass → återkallelse, svarens dataskydd, ägarskap vid skrivning och
validering av sidmarkör. Dessa
kontroller behöver HTTP för att visa att rätt användare, statuskod och svar kopplas ihop.

## Passlistans sidindelning

`GET /api/activities?timeZone=Europe%2FStockholm` ger `{ items, nextCursor }`, högst 25 pass
sorterade på `startedAt` och `id` fallande. Nästa sida hämtas med `beforeStartedAt` och `beforeId`
från `nextCursor`; null betyder att historiken är slut. Utelämnad tidszon ger UTC, webben skickar
webbläsarens IANA-tidszon. Listans `showMonth` och `showWeek` anger om fler egna pass finns i
samma lokala år respektive månad, även utanför sidan. Därmed ändras inte rubriknivåerna när
”Visa äldre pass” fyller på listan. Inga tidsserier eller rutter ingår i listsvaren.

`GET /api/activities/years?timeZone=Europe%2FStockholm` ger alla år med egna pass, nyaste först.
Passlistans `year=2024` begränsar både första och efterföljande sidor till det lokala året.
Årsväljaren visas när historiken innehåller flera år; ”Alla år” är standard. Valet sparas i
appens URL (`/app/activities?year=2024`) och följer med till passsidan och tillbaka.

Under målen växlar flikarna Lista och Kalender (`/app/activities?view=calendar`). Kalendern visar ett
år i taget (valt år, annars senaste året med pass) som tolv månader med veckor från måndag; dagar med
pass markeras och visar antalet när det är fler än ett, och klick öppnar dagens pass i en modal.
Tagg- och sportfiltret gäller båda vyerna, och vyn följer med till passsidan och tillbaka.
`GET /api/activities/calendar?year=2026&timeZone=…&tag=race&sport=running` ger årets egna pass
(id, sport, titel, start, lokal dag `day`, tid, distans och Garmin-märkning) äldst först, utan sidindelning.

Passsidans kilometersplits visas både i en utfällbar panel ovanför kartan och under diagrammen.
Knapparna Varv och Splits ligger bredvid varandra; högst en av panelerna kan vara öppen åt gången.
De interpolerar tid och höjd vid varje kilometergräns mellan
mätpunkterna; snittpulsen vägs med tiden. Ingen sträcka eller tid läggs till utanför
inspelningens uppmätta ändpunkter. Återställd distans eller icke stigande tid gör att
splitstabellen utelämnas. En avslutande rest under 50 meter visas inte.
Diagram använder kilometer som x-axel när distansen ökar utan återställningar,
annars förfluten tid. Tidsaxelns etikett följer appens valda språk.

## Standardsynlighet för pass

Profil → Inställningar → Profil och synlighet har ett automatiskt sparat standardval för nya pass: privat,
godkända följare eller alla inloggade löpare. Förvalet är privat. Manuella pass,
FIT-uppladdningar och Strava-importer läser det aktuella valet när varje pass sparas.
Enskilda pass kan fortfarande ändras via redigeringssidan.

**Tillämpa på alla pass** kräver bekräftelse och ersätter alla egna befintliga
synlighetsval med standardvalet. Andras pass påverkas inte. Standardvalet och
massändringen sparas atomiskt via `PUT /api/me/activity-privacy`
(`{ privacy: "private" | "followers" | "public", applyToAll?: boolean }`, CSRF krävs).
`GET /api/me` returnerar `defaultActivityPrivacy`, som även ingår i exportens
`profile.json`, och `activitiesWithOtherPrivacy`, antalet egna pass (utom Garmin-pass)
med annan synlighet. Bekräftelsen visar antalet och att de individuella valen inte går
att återställa i ett svep. När alla redan har standardvalet är knappen inaktiv och visar en bock efter
knappen; det gäller även utan pass. Migrering 015 lägger till `users.default_activity_privacy`.
Samma profillås som importerna gör att en pågående import inte kan spara ett
äldre standardval efter massändringen. Importdubbletter behåller sina val.

## Supporterskap

Frivilligt årligt supporterskap (brons 50 kr, silver 150 kr, guld 300 kr, valfritt belopp över 300 kr ger guld) ger bara en badge. Betalningen är inte byggd: knappen "Bli supporter" på Inställningar visar en modal om att betalningar inte är aktiverade än.

- `users.supporter_tier` (`bronze|silver|gold`) och `users.supporter_until` ska sättas av betalningsintegrationen. Badgen visas bara medan `supporter_until` ligger i framtiden.
- `users.supporter_badge_privacy` (`private|followers|public`, förval `private` eftersom nivån avslöjar beloppet) ändras med `PUT /api/me/supporter-badge-privacy` (`{ "privacy": "public" }`). `GET /api/me` returnerar `supporter: { tier, until, badgePrivacy }`.
- `supporterTier` finns i `/api/runners/{id}`, flödet och `/api/follows`, beräknat med `Supporters.VisibleTier` (samma regel som passens synlighet). Ingår i export och raderas med kontot.
- Lokalt: `update users set supporter_tier = 'gold', supporter_until = now() + interval '1 year' where id = '…'`.

## Profilbild

Inställningar → Profil och synlighet → Profilbild laddar upp en egen bild; utan bild visas initialerna. Ingen Gravatar: hashen av e-postadressen skulle låta andra koppla en känd adress till profilen, och Gravatar skulle se vem som tittar på vilken profil.

- Webben beskär till en kvadrat och skalar till 256 px JPEG i webbläsaren innan uppladdningen. Omkodningen tar bort EXIF, även GPS.
- `POST /api/me/avatar` (multipart, fältet `file`, högst 200 kB, måste vara JPEG; rate limiten `uploads`) och `DELETE /api/me/avatar` svarar `{ avatarId }`. Varje uppladdning får ett nytt slumpat `users.avatar_id` (migrering 030); den gamla filen raderas först, under profillåset.
- Bilden ligger i samma bucket som råfilerna (`avatars/<avatarId>.jpg`) och visas via `GET /api/avatars/{avatarId}` utan inloggning, med ett års webbläsarcache (rate limiten `public`). Bara den aktuella bilden svarar; en ersatt eller raderad ger 404.
- Bilden visas där namnet visas och har ingen egen synlighet: `avatarId` finns i `/api/me`, `/api/runners/{id}` (även publikt), flödet och `/api/follows`. Det slumpade id:t går inte att gissa.
- Bilden ingår i exporten som `avatar.jpg` och raderas med kontot.

## Dolda platser

Passens läsåtkomst följer synligheten och godkända följrelationer enligt ovan.
**Profil → Inställningar → Dolda platser** öppnar `/app/settings/privacy-places`, med
brödsmulan Profil → Inställningar → Dolda platser, där användaren kan spara flera namngivna
platser, till exempel Hem och Arbetsplats. Kartpunkten väljs genom klick, kartans
mittpunkt, webbläsarens platsåtkomst via **Använd min plats**, eller koordinatfält.
Platsåtkomst begärs bara vid knapptryck och hämtar en enda aktuell position, med
hög noggrannhet och 10 sekunders timeout. Punkten sparas först med Spara plats. Nekad/otillgänglig platsåtkomst och timeout visas
på valt språk utan att tidigare koordinater skrivs över. Varje plats har en radie på 50–5 000 meter, förval 200.
Platser kan redigeras och tas bort. Bara ägaren kan läsa och ändra dem via
`GET`/`POST /api/me/privacy-places` och `PUT`/`DELETE /api/me/privacy-places/{id}`.
Listan har `Cache-Control: no-store`. Migrering 014 skapar `privacy_places` med
ägarkoppling, kaskadradering, koordinat-/radiekrav och RLS utan Data API-grants.

Delade kartminiatyrer i flöde och profiler klipps i PostGIS mot ägarens aktuella platser,
med respektive radie, och minst 500 meter runt start och mål. Förenklingen sker före
klippningen; separata linjer sammanfogas aldrig över skyddade områden. Det gäller även
äldre pass. Zonernas mittpunkter flyttas med en hemlig, stabil förskjutning per användare
(`users.privacy_seed`, migrering 023): högst 500 m för start och mål och en radie för dolda
platser, och radien växer lika mycket (1 010 m respektive 2 × radien + 10 m). Klippunkterna ligger
på en cirkel med känd radie, så flera pass räcker för att hitta dess mitt; förskjutningen avgör
hur nära den verkliga punkten det tar en. Start och mål får egen förskjutning per ruta i ett
rutnät på ungefär 1 km, så att ett pass från en känd plats (ett lopp) inte avslöjar förskjutningen
hemma. Ett hem precis vid en rutgräns får två oberoende förskjutningar. Koordinaterna avrundas till
4 decimaler. Ägaren får hela rutten, och delade detaljsvar utelämnar GPS-tidsserier helt.
Sparade privata platsers namn och mittpunkter skickas aldrig till andra mottagare.

Projektionen ändrar bara svarets koordinater. Sparad rutt, råfil, tidsserier, rekord,
splits och totalsiffror behålls. Dolda platser är inte en garanti för anonymitet.
Namn och mittpunkter lämnas aldrig till andra användare. Kartbilderna kommer från
Kutas egen kartfil i R2 (se Karta), där Cloudflare ser vilket område som visas; namn och
sparade platser skickas inte dit som data. Ingen extern adressökning eller automatisk platsåtkomst används; platsåtkomst kräver
användarens knapptryck och webbläsarens behörighet.
Alla nya rutt-/export-/kartändpunkter måste använda samma skydd; originalrutt och råfil får inte
lämnas till andra mottagare. Ingen ny åtkomst följer av själva klippningen.

## Statistik över tid

`/app/stats` visar statistik med diagram och tabell för distans, träningstid,
snittempo och snittpuls samt antal pass. Perioden är rullande (senaste 4, 12 eller 26 veckorna, eller senaste 12 månaderna)
eller ett kalenderår; senaste 12 veckorna och löpning är förval (se [Sportgrupper](#sportgrupper)). Veckoperioder delas
i veckor som börjar på måndag, och månader och år delas i kalendermånader; den pågående veckan eller månaden markeras.
Summorna visar förändringen mot lika lång tid närmast före perioden (för kalenderår samma del av föregående år), så att
en påbörjad vecka jämförs rättvist. Årsväljaren innehåller hela den egna historiken. Tomma veckor och månader har noll mängd,
men saknat tempo och puls visas som `–`. En växel högst upp byter mellan Översikt och Analys
(`/app/stats/analysis`), som visar läget just nu: träningsbelastning och loppprognos, oberoende av periodvalet.

`GET /api/activities/statistics?from=2026-08-17&to=2026-11-09&bucket=week&sportGroup=running&timeZone=Europe%2FStockholm`
ger `{ buckets, total }` för den inloggade användaren: en rad per ISO-vecka eller kalendermånad (`bucket=week|month`,
`start` är periodens första dag) och summan för hela intervallet. `from` och `to` är lokala datum, `to` exklusivt, högst
400 dagar (annars `invalidStatisticsPeriod`; okänd indelning ger `invalidStatisticsBucket`). Börjar `from` mitt i en vecka
eller månad börjar första raden ändå där, men räknar bara pass från `from`. Utelämnad sportgrupp
betyder alla sporter (okänd grupp ger `invalidSportGroup`) och utelämnad tidszon UTC. Summeringen sker i Postgres över hela
intervallet, oberoende av passlistans sidindelning, utan att skicka tidsserier eller rutter till webben.
Tid inkluderar pauser. Tempo är summan av tid delad med summan av distans för pass med
positiv tid och distans (sekunder per kilometer). Snittpuls tidsvägs bara över pass med
positiv tid och sparad puls. Alla sporter kombinerar även tempo och puls mellan sporter; samlade multisportpass ingår inte i snittempot. Vid gruppfilter räknas hela pass i gruppen och motsvarande delar av multisportpass i andra grupper, med puls från delens mätpunkter. Totalvyn räknar varje pass en gång.
Veckor, månader och periodgränser följer webbläsarens tidszon, och hela passet tillhör startveckan eller startmånaden.
Statistikcachen uppdateras vid skapande, import och radering av pass och rensas när
pulssamtycke återkallas.

**År för år** på samma sida visar ackumulerad distans, tid eller antal pass per år på samma
kalender för vald sportgrupp. Diagrammet visar valt år framhävt och upp till fem senaste år.
Tabellen jämför alla år till och med dagens månad och dag (så att skottdagen inte förskjuter
jämförelsen), med förändring mot året före och helårssumma.
`GET /api/activities/statistics/days?sportGroup=running&timeZone=Europe%2FStockholm` ger
summor per lokal dag (`day`, `count`, `distanceM`, `durationS`) över hela den egna historiken,
bara dagar med pass. Summeringen följer målens regler (`SportGroups.EffectiveCte`): hela passet
hör till startdagen, en grupp räknar matchande delar av multisportpass och utan grupp räknas
varje pass en gång. Okänd grupp ger `invalidSportGroup` och ogiltig tidszon `invalidTimeZone`.

## Jobbkö

Långa jobb körs i en egen kö i Postgres (`JobQueue`, `JobWorker`, migrering 020), inte i webbanropet. Hangfire och
Wolverine valdes bort: Hangfire sparar undantagstexter och stackspår i egna tabeller, vilket strider mot den privata
loggningen, och Wolverine är ett helt meddelanderamverk. Kön är en tabell `jobs`:

- **Rader:** `kind` (`strava_import` eller `recalculate`), `status` (`queued`, `running`, `succeeded`, `failed`),
  `progress` och `result` som JSON, en stabil `error_code`, försök och tider. Aldrig filinnehåll eller undantagstexter.
  Högst ett aktivt jobb av varje slag per användare (unikt delindex). Jobben kaskadraderas med kontot, och avslutade
  jobb rensas efter 30 dagar.
- **Arbetare:** en bakgrundstjänst i API-processen tar nästa jobb med `for update skip locked` och håller det med ett lås
  på två minuter som förnyas vid varje förlopp. Dör maskinen går låset ut och jobbet tas upp igen. Vid nedstängning
  lämnas jobbet tillbaka utan att försöket räknas. Oväntade fel ger högst tre försök med ökande väntetid och loggas som
  den fasta händelsen `job_failed` med undantagsprojektionen; därefter `jobFailed`.
- **Importarkiv:** ZIP-filen ligger på maskinens lokala disk (`Jobs:ImportDirectory`, förval en katalog i
  temp-katalogen) medan jobbet väntar och körs, och raderas när jobbet avslutas, när kontot raderas och vid arbetarens
  timvisa städning. Arkiv på upp till 500 MB passar inte objektlagringens storleksgräns, och filen innehåller puls.
  Driften har en maskin; fler maskiner kräver att arkivet flyttas till objektlagring.
- **Omberäkning:** `POST /api/activities/recalculate` (CSRF) köar omberäkning av rekord och löpintensitet för alla egna
  pass med tidsserier, 50 åt gången. Ett aktivt jobb returneras i stället för ett nytt. Förloppet sparar senaste passet,
  så ett avbrutet jobb fortsätter där det slutade. Migrering 020 köar en omberäkning för alla användare med tidsserier.
  Knappen finns under Inställningar → Data och integritet → Räkna om aktiviteter.
- **Läsning:** `GET /api/jobs` ger användarens senaste jobb av varje slag och `GET /api/jobs/{id}` ett eget jobb
  (annars 404). Webben frågar varje sekund medan ett jobb är aktivt.
- **Tester:** `Jobs__Enabled=false` stänger av arbetaren i testappen, och testerna kör jobben med
  `JobWorker.RunAllAsync` för en användare.

## Träningsbelastning

Analys (`/app/stats/analysis`) visar kondition, trötthet och form för löpning, och Pass visar en varning högst upp när
konditionen ökar snabbt. Modellen är TrainingPeaks förenkling av Banisters impuls–respons-modell:

- **Belastning per pass (rTSS):** `∫ v² dt / vT² / 36`, där `vT` är tröskelfarten. En timme i tröskelfart ger 100.
  `activities.run_intensity` (migrering 019) lagrar `∫ v² dt` (m²/s) för passets löpning, eller bara löpdelarna i
  ett multisportpass. Den räknas av `BestEfforts.ReplaceAsync`, alltså vid FIT- och Strava-import, uppdelning och
  omberäkning av rekord. Farten jämnas ut över minst 30 s, ett uppehåll över 10 s (autopaus) bildar ett eget
  avsnitt, och trasiga värden bryter strömmen. Pass utan distansström (manuella och äldre pass) uppskattas ur
  snittfarten, `distans² / tid`, utan backjustering.
- **Backar:** varje avsnitts fart multipliceras med `C(i) / 3,6`, där `C(i)` är Minettis energikostnad för löpning
  (J/kg/m) vid lutningen `i = Δhöjd / Δdistans` ur höjdströmmen, begränsad till ±45 %. Uppför ger högre belastning
  och svag nedförslutning lägre. Saknad höjd räknas som plan mark. Äldre pass får justeringen vid omberäkning av rekord.
- **Tröskelfart:** `users.threshold_speed_m_s` om användaren angett den, annars farten som VDOT-prognosen räcker
  till i en timme (`RacePredictor.ThresholdSpeed`). Utan någon av dem finns ingen belastning. Samma tröskel gäller
  hela historiken.
- **Kondition (CTL) och trötthet (ATL):** exponentiella medelvärden av daglig belastning med 42 respektive 7 dagar
  (`x += (belastning − x) / N`), från första dagen med löpning till idag i webbläsarens tidszon. Form (TSB) är
  kondition minus trötthet samma dag.
- **Varning:** när konditionen har ökat mer än 7 på en vecka, och först efter mer än 42 dagars historik så att en ny
  användare inte varnas medan konditionen byggs upp från noll. Texten är information, inte ett medicinskt råd.

`GET /api/training-load?timeZone=Europe%2FStockholm` ger `thresholdSpeedMS`, `thresholdSource`
(`manual`/`prediction`/`null`), `predictedThresholdSpeedMS`, `days` (de senaste 182 dagarna med `load`, `fitness`,
`fatigue` och `form`), `rampPerWeek` och `warning`. `PUT /api/me/threshold-speed` med `{ "thresholdSpeedMS": 4 }`
sparar en egen tröskelfart (1:40–60:00 per km, annars `invalidThresholdSpeed`), och `null` går tillbaka till
prognosen. Tröskelfarten ingår i dataexportens `profile.json`. Bara egna pass räknas.

## Analys, utmärkelser och årssammanfattning

Alla regler är fasta och räknas på användarens egna pass oavsett källa (FIT, Strava, Garmin). Ingen AI används.
Vyerna visar `GarminDerivedNote` när kontot har Garmin-data, och passsidans analys har passets Garmin-märkning.

**Analys per pass** (`components/activity-analysis.tsx`, regler i `web/src/lib/activity-analysis.ts`) visas på
passsidan för löpning (även en vald löpdel i ett multisportpass) och räknas i webbläsaren ur tidsserierna och varven
som passsidan redan får:

- **Fartfördelning:** tiden för första och andra halvan av distansen på en rörelseklocka (avbrott över 10 s och
  fart under 0,5 m/s räknas inte). Inom ±2 % är jämn fart, annars negativ eller positiv split. Sista femtedelen
  jämförs med resten och nämns vid minst 5 % skillnad. Kräver minst 3 km.
- **Pulsdrift (aerob frikoppling):** lutningsjusterad fart per hjärtslag i första mot andra halvan av rörelsetiden,
  efter 10 minuters uppvärmning, i fönster om ca 30 s med fart över 1,5 m/s. Kräver minst 20 minuter kvar och en
  variationskoefficient i lutningsjusterad fart på högst 0,15 (intervaller räknas inte). Under 5 % är stabilt,
  5–10 % drift och över 10 % tydlig drift. Bara ägaren får puls, så andra ser aldrig driften.
- **Intervaller:** varvens fart delas i en snabb och en långsam grupp där skillnaden mellan grupperna är störst
  (varv under 10 s eller 50 m räknas inte). Snabba varv i följd blir en repetition. De snabba måste i snitt vara minst
  20 % snabbare och ge minst två repetitioner, och ingen repetition får vara längre än 25 minuter eller vilan mellan två repetitioner längre än 10 minuter (annars är det ett långpass med backar, gång eller stopp, till exempel ett terrängmaraton med autovarv). Tabellen visar distans, tid, tempo, puls och vila; texten snabbaste
  och långsammaste tempo och sista repetitionen mot snittet av de andra.

**Aerob effektivitet** (Statistik → Analys, `AerobicEfficiency.cs`, migrering 031): `BestEfforts.ReplaceAsync`
sparar `activities.aerobic_speed_m_s` och `aerobic_hr` för vanliga löppass (inte multisport) med samma fönster,
uppvärmning och jämnhetskrav som pulsdriften, med Minettis backjustering som träningsbelastningen. Värdena härleds ur
puls och nollställs när pulssamtycket återkallas. Äldre pass får dem vid omberäkning av rekord.
`GET /api/aerobic-efficiency?timeZone=` ger det senaste årets egna pass som `[{ activityId, day, speedMS, hr }]`.
Webben (`lib/aerobic-efficiency.ts`) väljer referenspulsen som medianen avrundad till 5, tar bara pass inom ±10 slag,
skalar farten med referens/puls och visar månadens median som tempo vid samma puls (minst tre pass).

**Utmärkelser** (`Achievements.cs`, migrering 032) räknas vid läsning ur passen, så raderade pass tar med sig sina
utmärkelser. `GET /api/achievements?timeZone=` ger `[{ key, kind, threshold, year, achievedAt, activityId, seen }]`,
nyast först:

- `firstDistance`: första 5 km, 10 km, halvmaran och maran ur `best_efforts`; `firstUltra`: första löppass över 45 km.
- `runDistance`: total löpdistans 100, 470, 1 000, 1 572, 5 000, 10 000 och 40 075 km (jämförs i webben med
  Vasaloppet, Stockholm–Göteborg, Sveriges längd, en fjärdedel av och ett helt varv runt jorden).
- `yearRunDistance`: 500, 1 000, 2 000 och 3 000 km löpning under ett kalenderår.
- `climb`: höjdmeter under ett kalenderår i alla sporter, 2 097 (Kebnekaise), 4 806 (Mont Blanc), 8 849 (Mount
  Everest) och 17 698 m.
- `activityCount`: 10, 50, 100, 250, 500 och 1 000 aktiviteter. `monthRunDistance`: första kalendermånaden med
  100, 200 eller 300 km löpning. `weekStreak`: första gången 4, 12, 26 eller 52 veckor i rad (måndag–söndag) med
  minst en aktivitet. `earlyBird`/`nightOwl`: första aktiviteten som startar 03–06 respektive efter 22.

Löpning räknas som statistiken: löpgruppen och löpdelar i multisportpass, medan antal, höjdmeter, veckor och tid på
dygnet gäller hela pass. Utmärkelsen hör till passet som når gränsen. `achievement_views` sparar bara vilka nycklar
användaren sett. `AchievementTeaser` i `Layout` hämtar listan en gång per appstart (inte vid passändringar) och visar
osedda i en modal. Utmärkelser som nåtts före `users.achievements_since` (migrering 033: driftsättningen för befintliga konton, annars när profilen skapades) räknas som sedda, så modalen firar bara nya bedrifter och inte gammal historik eller ett importerat Strava-arkiv; profilen listar fortfarande alla. Stängning skickar `POST /api/achievements/seen` med `{ keys, timeZone }`, som bara sparar nycklar
som faktiskt är uppnådda. Alla utmärkelser listas på den egna profilen. Sedda nycklar ingår i exporten
(`achievements-seen.json`) och kaskadraderas med kontot.

**Årssammanfattning** (`/app/stats/year/$year`, `YearReview.cs`, länk på Statistik för innevarande år och
föregående år fram till och med mars): `GET /api/activities/year-review?year=&timeZone=` ger egna pass under
kalenderåret i tittarens tidszon: totaler (antal, distans, tid, höjdmeter, aktiva dagar) och föregående års totaler,
sportgrupper och månader som statistiken, favoritveckodag och tid på dygnet (morgon 05–10, mitt på dagen 10–14,
eftermiddag 14–18, kväll 18–22, natt), flest dagar och veckor i rad, längsta löpning och aktivitet, tävlingar
(taggen `race`), årets bästa tid per rekorddistans med personbästa (inga snabbare tidigare insatser; pass undantagna
från rekord räknas inte) och den utrustning som använts längst. `complete` är falskt för innevarande år. Webben
(`lib/year-review.ts`) jämför distansen med maraton, Stockholm–Göteborg, Sveriges längd eller jordens omkrets och
höjdmetrarna med Eiffeltornet, Kebnekaise eller Mount Everest, och visar årets utmärkelser.

## Träningsmål

**Profil → Mål → Nytt mål** (eller Nytt mål överst på Pass) skapar ett mål: distans (km), tid (minuter) eller antal pass per vecka,
månad eller år, för en grupp av träningsformer eller alla. Målet upprepas varje period eller gäller en gång,
för perioden då det skapas (exempelvis 1 000 km i år). Pågående mål visas som kort med
förloppsstapel under Mål på profilen och på Pass; ett uppnått mål får ”Hurra! Målet är nått”. Upprepade mål
visar även förra periodens resultat när det nåddes och hur många perioder i rad målet nåtts
(från två). Ett avslutat engångsmål visas nedtonat tills det tas bort. Klick på kortet öppnar
redigering och borttagning i samma modal. Inga mål ger ingen sektion.

Varje mål har en egen synlighet i måldialogen: bara jag (förval), godkända följare eller alla
inloggade med länken. Det finns ingen global inställning för mål. Egna målkort visar valet.
Profiler visar de mål besökaren får se med förlopp under rubriken Mål, utan redigering; inga
synliga mål ger ingen sektion. Läsregeln är passens: ägaren, `public` för alla inloggade och
`followers` bara vid godkänd följrelation.

Mål gäller [sportgrupper](#sportgrupper), inte exakta sporter, och dialogen visar vilka
träningsformer den valda gruppen räknar.

Summeringen följer statistikens regler: hela passet hör till perioden där det startar, tid
inkluderar pauser, redigerade multisportdelars distans används och ett gruppmål räknar
matchande delar av multisportpass. Utan grupp räknas varje pass en gång. Veckor börjar på
måndag och perioder följer webbläsarens tidszon. Ett mål räknas från början av perioden då det
skapas, även pass före skapandet (ett årsmål i oktober räknar från 1 januari), men aldrig
tidigare perioder. Ett nytt upprepat mål visar därför förra perioden och raden först från nästa
period. Raden räknar uppnådda perioder bakåt från förra perioden till första missade eller
startperioden, plus den pågående när den är uppnådd. Ett ändrat mål behåller sitt startdatum.

API (inloggning krävs; skrivningar kräver CSRF; bara egna mål, andras ger 404):

- `GET /api/goals?timeZone=Europe%2FStockholm` ger målen med `current` och `previous`
  (`{ start, end, value, achieved }`, `end` exklusivt; `previous` är null för engångsmål och under startperioden),
  `streak` och `ended`.
- `POST /api/goals` och `PUT /api/goals/{id}` tar `{ sportGroup, metric, period, target, recurring, timeZone, privacy }`.
  Utelämnad `privacy` ger privat vid skapande och behåller valet vid redigering. Svaren har `privacy`.
  `sportGroup` är en gruppnyckel eller null = alla, `metric` är `distance`, `duration` eller `count`, `period` är `week`,
  `month` eller `year`, `target` i meter, sekunder eller hela pass (högst 100 000 000 m eller s, respektive
  10 000 pass). `timeZone` avgör startdatumet vid skapande. Ogiltiga värden ger `invalidGoal`.
- `DELETE /api/goals/{id}`.
- `GET /api/runners/{id}/goals?timeZone=…` ger löparens mål som besökaren får se, filtrerade
  per mål (ägaren ser alla); okänd löpare ger 404.

Migrering 016 skapar `goals` med ägarkoppling, kaskadradering vid kontoradering och RLS utan
Data API-grants; migrering 017 lägger till `goals.privacy`, som även ingår i exportens `goals.json`. Radering av alla pass behåller målen. Målen ingår i exportens `goals.json`.
Periodlogik, grupper och validering testas som enhetstester, summering per tidszon, sportgrupp, multisportdelar
och ägarskap i lokal Postgres, och HTTP-testet täcker routing, statuskoder, svarskontrakt och
att en annan användare varken ser eller ändrar målet. Synligheten per mål (ägare, godkänd och
väntande följare, okänd, oförändrat val vid redigering) testas i Postgres; ett HTTP-test visar
att en annan inloggad bara får löparens publika mål.

## Sportgrupper

Mål och statistik räknar grupper av träningsformer, inte exakta sporter: Löpning räknar löpning,
terränglöpning och virtuell löpning; Cykling alla cykeltyper inklusive virtuell; Gång och
vandring även snöskovandring; Längdskidåkning även topptur och rullskidor; Paddling kanot, kajak
och SUP; Rodd även virtuell rodd. Simning i bassäng och öppet vatten är samma sport (`swimming`),
eftersom Garmin anger vattentypen som undertyp och Strava inte skiljer dem åt. Övriga sporter är
egna grupper med sportens nyckel. Grupperna definieras bara i `SportGroups.cs`, även med Garmins
råa sportnamn (exempelvis `crosscountryskiing`); `GET /api/activities/sport-groups` ger
`[{ key, sports }]` och webben namnger grupperna under `sportGroups` i språkfilerna. Passlistans
sportfilter och passens egna sport är oförändrade.

## Applikationsloggar och integritet

API:t ersätter .NET:s loggproviders med `PrivateLogProvider`. Den skriver JSON med fasta händelser och operationer för start, stopp, requestfel, startfel och migreringsfel. Requestfel har även HTTP-status och tidsåtgång i millisekunder. Loggformatterare, godtyckligt loggstate, scopes och kategorinamn används aldrig, även om loggnivån höjs. DbUp:s egen konsolloggning är avstängd. Samma skydd används lokalt och i drift.

Undantag projiceras till en begränsad modell: typnamn, metodernas typ- och metodnamn samt relativa källkodssökvägar och radnummer från debugsymboler. Bara källfiler under projektets build-rot får en sökväg; saknade symboler eller externa filer ger ingen fil- eller radinformation. Dynamiska metoder och typnamn från dynamiska assemblies utelämnas eller ersätts med en fast etikett. Generiska typer visas utan konkreta typargument. Felkedjor, inklusive alla grenar i `AggregateException`, begränsas till totalt åtta undantag och högst 32 stack frames per undantag. Undantagsmeddelanden, formaterade stackspår, `Data`, `Source`, `HelpLink` och andra godtyckliga egenskaper serialiseras aldrig. Om metadata inte kan läsas används en fast reservetikett.

Inga request bodies, URL:er, querysträngar, headers, cookies, tokens, mejladresser, lösenord, puls, GPS-rutter eller uppladdade filers namn/innehåll skrivs av applikationsloggningen. Automatiska HTTP-, databas- och ramverksloggar släpps inte igenom. Felsökning använder feltyp, kodposition och driftmått; råa felmeddelanden får undersökas med syntetiska lokala reproduktioner.

Nya logghändelser ska använda granskade strukturerade fält utan användarvärden och testas med känsliga syntetiska värden. Okända fält utelämnas som standard. Använd inte `Console.WriteLine`/stderr för dynamiska värden, serialisera inte råa undantag och lägg inte till andra loggproviders vid sidan av integritetsgränsen. Regex-maskning av fritext är inte en garanti mot personuppgifter.

Framtida loggmottagare får samla in den begränsade konsolutmatningen. En direkt SDK-integration som Sentry måste granskas separat: automatisk insamling av exceptions, requests, breadcrumbs, användarkontext och tracing passerar inte nödvändigtvis loggprovidern. Aktivera den inte utan en testad tillåtelselista för vad som får skickas. Detta skydd omfattar Kutas applikationsloggar, inte Flys egna proxy-/plattformloggar, Supabases loggar eller historiska loggar.

## Multisport och uppdelning av importerade pass

En multisport-FIT importeras som ett pass med ordnade sportdelar, inklusive registrerade växlingar. Simning → cykling → löpning identifieras som triathlon, kombinerad simning/löpning som swimrun och övriga kombinationer som multisport. Sessionernas start, tid och distans bevaras; kadens tolkas för respektive sport. Vid multisport-FIT i ett Strava-arkiv används FIT-filens sportdelar, starttid och totalsammanfattning; CSV-titeln behålls. Stravas separata aktiviteter slås inte ihop automatiskt.

På skapa- eller redigeringssidan väljer du **Dela upp i sporter**, väljer passtyp och justerar sportbyten med reglage eller tid (h:mm:ss) från passets start. **Dela här** delar den valda delen i mitten; för swimrun får den nya delen motsatt sport. **Slå ihop med föregående** tar bort en gräns och behåller föregående sport. Klicka på en del för att granska dess karta och diagram innan du sparar. Ändringarna är ett lokalt utkast tills sidans gemensamma **Spara** används; **Avbryt** lämnar det sparade passet oförändrat. Passsidan visar bara resultatet och val av sportdel, med länken **Redigera aktivitet** till uppdelningsverktyget. Hela passets karta använder olika färger för sporterna. Simtempo visas per 100 meter, cykling i km/h och löpning per kilometer.

Uppdelningen kräver minst två sporter och en sammanhängande, stigande tidsserie med fullständig, icke minskande distans. Delarna täcker hela inspelningen utan luckor eller överlapp; gränser mellan mätpunkter interpoleras. Högst 200 delar stöds. Sammanfattningar utan tidsserier kan inte klippas. Funktionen ändrar sportindelningen och reparerar inte felaktig GPS eller simdistans.

FIT-uppladdning tar ett valfritt multipartfält `parts` med `{ kind, parts: [{ sport, startS, endS, useEquipment }] }`. `kind` är `triathlon`, `swimrun` eller `multisport`; sport kan även vara `transition`. Triathlon kräver simning, cykling och löpning i den ordningen (med valfria växlingar). Swimrun tillåter simning och löpning (med valfria växlingar). Passets valda utrustning räknas bara på delar där `useEquipment` är sant; olika utrustningsval per del stöds ännu inte. FIT-importen markerar löpning/gång/vandring som standard och även simning för swimrun. Växlingstid finns kvar i den totala förflutna tiden.

Redigeringssidan sparar uppdelningen som `multisport` tillsammans med titel, beskrivning, taggar och utrustning i `PUT /api/activities/{id}`. **Återställ importerad version** kräver bekräftelse, förhandsvisar originaldelarna och skickar `restoreParts: true` först när formuläret sparas. Allt sparas atomärt. De separata API-anropen `PUT`/`DELETE /api/activities/{id}/parts` finns också för uppdelning/återställning. Originalets sammanfattning, tidsserier, filhash och råfil behålls vid redigering. Inga extra pass skapas. Utrustningstotaler och distans uppdateras från den gällande uppdelningen; löprekord räknas om i samma transaktion och kan aldrig sträcka sig över flera löpdelar. Ändringsanropen kräver inloggning, ägarskap och CSRF-token. Sportdelar innehåller ingen kopia av pulsdata, så återkallat pulssamtycke fortsätter att radera all sparad puls på samma ställen.

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

### Kontoradering

Inställningar → Data och integritet → Radera data → **Radera konto** kräver att användaren skriver sitt lösenord. `DELETE /api/me` kräver inloggning, CSRF-token och `{ password }`, som API:t kontrollerar mot Supabase (fel lösenord ger `403 wrongPassword`; auth-gränsen per IP gäller), och använder endast den inloggade användarens id. Under samma profillås som importen raderas råfilerna via objektlagringen, därefter raderas `auth.users` direkt i databastransaktionen. Befintliga FK-kaskader tar bort profil, utrustning, pass, tidsserier, varv, sportdelar, rekord och Supabase-/Kuta-sessioner. Cookien och webbens sessionscache rensas; gamla cookies avvisas vid nästa anrop.

Om filrensningen misslyckas behålls kontot och filreferenserna för ett nytt försök. Redan raderade filer kan inte återställas om ett senare steg misslyckas, men raderingen kan köras igen. Leverantörsloggar och säkerhetskopior omfattas inte av appens radering; se integritetsunderlaget nedan. Ingen ny migrering eller separat Auth-adminnyckel behövs.

### Lösenordsåterställning

Välj **Glömt lösenord?** på inloggningssidan. Mejlet finns i instansens Mailpit och länkar till `/api/auth/reset-password`. API:t verifierar länken och ger en separat krypterad HttpOnly-cookie som gäller i högst 15 minuter och bara för lösenordsbytet. Efter bytet avslutas kontots Kuta-sessioner och användaren loggar in med det nya lösenordet. Om anslutningen bryts under lösenordsbytet avslutas sessionerna också, eftersom bytet kan ha hunnit lyckas hos Supabase. Begär då en ny återställningslänk vid behov. Både begäran och lösenordsbytet kräver CSRF-token; ogiltiga, använda och utgångna länkar kräver en ny begäran.

Den lokala mejlmallen anges i `supabase/config.toml`. Om en redan körande miljö saknar återställningsmallen måste dess ägare stoppa och starta just den Supabase-instansen för att läsa in konfigurationen. Ändra inte en lånad instans.

### Mejlmallar

`supabase/templates/confirmation.html` och `recovery.html` är fristående svenska HTML-mallar för de två mejlflöden som appen använder. De följer appens neutrala ytor, rundade hörn och orange primärfärg, med inline-stilar som grund och mörkt tema i mejlklienter som stöder `prefers-color-scheme`. De använder systemtypsnitt och inga bilder eller externa typsnitt. Layouten är tabellbaserad och anpassas till skärmbredden; varje knapp har en kopierbar reservlänk.

Behåll `{{ .SiteURL }}` och `{{ .TokenHash }}` i både knappen och reservlänken: bekräftelse går till `/api/auth/confirm`, återställning till `/api/auth/reset-password`. Ersätt inte dessa med `{{ .ConfirmationURL }}`, eftersom Kutas API ska ta hand om sessionen. Ämnesraderna finns i `supabase/config.toml`; kopiera även dem när mallarna läggs in i det hostade projektets **Authentication → Email Templates**. En ändring i repot uppdaterar inte hostade mallar automatiskt. Kontrollera båda mejlen i lokal Mailpit enligt [lokal körning](#köra-lokalt), och i faktiska mejlklienter före driftsättning eftersom deras HTML- och mörkerlägesstöd varierar.

Tester (kräver `supabase start`, med Storage påslaget): `dotnet test --project api/Kuta.Api.Tests`, eller `KUTA_PORTS=BAS dotnet test …` mot en egen instans.

API-jobbet i GitHub Actions använder `KUTA_PORTS=18000` och flyttar Supabase-konfigurationens portar i jobbets checkout. Portarna 18020–18029 ligger utanför Linux normala intervall för tillfälliga utgående anslutningar, så nedladdning av Docker-images inte kan ta databasporten före start. Lokal standardkonfiguration ligger kvar på 553xx.

Frontendkontroller med Node-versionen i `.nvmrc`: kör `npm ci` i `web/`, sedan
`npm run verify`, som kör samma steg som CI: `format:check`, `lint`, `test` och
`build` (inklusive `tsc`).
`npm run format` formaterar webbfilerna med Prettier. API-klientens tester använder
Nodes inbyggda testverktyg och mockade nätverksanrop; de kräver inte Supabase.
GitHub Actions kör frontendkontrollerna i `.github/workflows/frontend.yml` vid
ändringar i `web/`, `.nvmrc` eller workflowfilen. API-jobbet i
`.github/workflows/api.yml` bygger och kör hela `api/Kuta.Api.Tests` i Release vid
ändringar i `api/`, `supabase/`, `global.json` eller workflowfilen. Båda körs på
pull requests med dessa sökvägsfilter; `Dockerfile`, `.dockerignore` och
`.github/workflows/deploy.yml` startar båda.
Ändringar i enbart `.md`-filer startar inga PR-kontroller. Vid varje push till
`main` anropar `.github/workflows/deploy.yml` båda jobben utan sökvägsfilter och
deployar bara om båda lyckas; se [Driftsättning](#driftsättning-flyio).

API-jobbet installerar .NET enligt `global.json` och Supabase CLI 2.72.7, startar
en tillfällig lokal Supabase med Auth, Mailpit och Storage, läser dess lokala
nycklar och väntar på auth efter omstart innan testerna körs. Testerna körs
sekventiellt (`-- --parallel none`). Inga produktionshemligheter eller hostade
tjänster används. Supabase stoppas även om testerna misslyckas.

Footern visar copyright och webbens byggnummer (`ÅÅÅÅMMDD.ttmmss`, UTC), som sätts
automatiskt av Vite vid bygget, även i Docker. I utvecklingsläge visas `Version utveckling`.

## Råfiler vid misslyckad import

Om uppladdning eller databascommit misslyckas släpps transaktionen innan en ny
anslutning kontrollerar om råfilen används av ett sparat pass. Kontrollen håller
profilens lås, så en samtidig import inte kan återanvända filnyckeln medan den
städas. Ett förlorat commitsvar får därmed inte radera råfilen för ett sparat pass.
Om databasen inte kan bekräfta utfallet lämnas filen kvar och felet loggas av
API:t. En sådan fil kan behöva städas manuellt när databasen åter är tillgänglig;
kontrollera först att inget pass refererar till nyckeln.

## Utrustning och användning

Aktiv utrustning visas före pensionerad. Inom grupperna behålls ordningen efter när utrustningen lades till.

Lägg till utrustning i modalen under Inställningar → Utrustning och koppla den till manuella pass eller FIT-importer. Välj ”Redigera utrustning” i utrustningens meny för att ändra namn, märke och tidigare användning i samma modal. Passkopplingar och pensionsstatus behålls. Utrustning kan även skapas direkt med ”Skapa ny” vid utrustningsfältet när ett pass skapas eller redigeras; den nya utrustningen väljs automatiskt utan att aktivitetsutkastet försvinner.
Kopplingarna kan också ändras på passsidan. Ett pass kan använda flera
utrustningsdelar. Skor (`shoe`) är den första typen; nya typer läggs till i
`EquipmentEndpoints.Kinds` och i båda språkfilernas `equipmentKinds`. Typväljaren i formuläret visas först när fler än en typ finns.

Migrering 009 gör om `shoes` till `equipment` och bevarar befintliga skor,
startdistanser och passkopplingar. `activity_equipment` kopplar pass och utrustning;
sammansatta främmande nycklar kräver samma ägare på båda sidor.

`distanceM` och `durationS` är startvärdena plus summan av de kopplade passens
`distance_m` och `duration_s` (förfluten tid inklusive pauser). Samma pass räknas
högst en gång per utrustning. Totalerna beräknas vid läsning, så ändrade kopplingar
och raderade pass slår igenom direkt. Radering av alla pass behåller utrustningen
och dess startvärden. Tidigare användning ska bara avse sådant som inte redan
finns i kopplade Kuta-pass, annars räknas den dubbelt.

Alla anrop kräver inloggning; skrivningar kräver `X-CSRF-TOKEN`:

| Anrop | Resultat |
|---|---|
| `GET /api/equipment` | Egen utrustning med startvärden och total användning. |
| `GET /api/equipment/last-used-shoe` | Senast använda aktiva sko, sorterad på passets starttid; `{ "id": null }` om ingen finns. |
| `PUT /api/equipment/{id}` | Uppdaterar egen utrustning med samma fält och validering som vid skapande. Annans eller okänd utrustning ger 404. |
| `PUT /api/equipment/{id}/retire` | Pensionerar egen utrustning och behåller gamla kopplingar. |
| `DELETE /api/equipment/{id}/retire` | Återaktiverar egen utrustning så att den kan väljas igen; historik och totaler behålls. |
| `DELETE /api/equipment/{id}` | Raderar egen utrustning och dess kopplingar, men behåller passen. |
| `GET /api/equipment/kinds` | Tillåtna typer. |
| `POST /api/equipment` | Skapar utrustning med `name`, `kind`, valfritt `brand`, `initialDistanceM` och `initialDurationS`. Båda startvärdena är 0 om de utelämnas. |
| `PUT /api/activities/{id}` | Sparar titel, beskrivning, taggar och utrustning tillsammans med `{"title":"Morgonpass","description":null,"tags":["race"],"equipmentIds":["uuid"]}`; tomma listor tar bort respektive val. Annans eller okänt pass ger 404. |

Vid skapande av pass skickas `equipmentIds` som JSON-array (manuellt) eller
upprepade formulärfält (FIT). Okänd eller annans utrustning avvisas utan att
passet eller kopplingarna ändras. API:t använder SI-enheter; webben visar kilometer.
Valfri `privacy` (`private`, `followers` eller `public`) i samma JSON eller formulär sätter passets synlighet direkt; utelämnad ger användarens standardsynlighet, och Garmin-pass blir alltid privata.

Passsidan visar bara kopplad utrustning och valda taggar. Knappen Redigera aktivitet
öppnar `/app/activities/$activityId/edit`, där titel, beskrivning, taggar och utrustning ändras med gemensamma Spara och
Avbryt. Spara uppdaterar allt i samma transaktion; Avbryt lämnar passet oförändrat.

## Taggar på aktiviteter

Ett pass kan ha flera taggar: `race` (Lopp), `workout` (Intervaller/kvalitet),
`long_run` (Långpass) och `easy` (Lugnt). Välj dem vid manuell registrering eller
FIT-uppladdning, ändra dem via passsidans Redigera aktivitet och filtrera passlistan efter en tagg.
Listans taggfilter sparas i URL:en (`/app/activities?tag=race`), kan kombineras
med `year` och följer med till pass, skapandeformulär och tillbaka till listan.

`activities.tags` är `text[]`, med tom array som standard och GIN-index.
API:t validerar mot `ActivityInput.Tags` och tar bort dubbletter; databasen har
avsiktligt ingen lista över tillåtna nycklar. Webben hämtar alternativen från
`GET /api/activity-tags`. En ny tagg kräver en nyckel i kodlistan och en översättning
i vardera `tagNames` i svenska och engelska språkfilen, ingen ny migrering.

Alla anrop kräver inloggning och gäller bara egna pass. Skapande tar `tags` som
JSON-array för manuella pass eller upprepade formulärfält för FIT. Ändra med
`PUT /api/activities/{id}` med `title`, `description`, `tags` och `equipmentIds`, exempelvis
`{"title":"Morgonpass","description":null,"tags":["race","workout"],"equipmentIds":[]}`; tom array
tar bort alla taggar. Skrivningar kräver `X-CSRF-TOKEN`.

### Träningsformer

Träningsformen visas med en ikon efter titeln i passlistan och på passidan, med översatt namn som skärmläsaretikett och vid hovring. Efter år och taggar finns ett filter för träningsform. År, tagg och träningsform bevaras i URL:en mellan passlista, passida och formulär. `GET /api/activities?sport=cycling&tag=race&year=2013` kombinerar filtren före sidindelning och beräkning av periodrubriker. Sportvärdena finns i `ActivitySports` (API) och `web/src/lib/sports.ts` (webb); sportval och översättningar täcker Stravas träningsformer samt Annat.

`GET /api/activities?tag=race` filtrerar före sidindelningen på 25 pass. Sidmarkörer och periodrubriker gäller det filtrerade urvalet.
`GET /api/records?tag=race&year=2026` ger bästa insatsen per distans i lopp under
UTC-året; utelämnat år ger personbästa i lopp. Filtren använder
`tags @> ARRAY['race']`, som stöds av GIN-indexet. Ändrade taggar slår igenom i
rekordurvalet direkt, utan omberäkning av insatserna.

## Bästa insatser och rekord

FIT- och Strava-importen räknar bästa insats för löpning över 1 km, 1 mile, 5 km, 10 km,
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

Alla anrop kräver inloggning. Rekord och historik gäller den egna användaren;
passets bästa insatser följer passets läsregel:

| Anrop | Resultat |
|---|---|
| `GET /api/activities/{id}/best-efforts` | Passets bästa insatser, sorterade på distans; 404 för okänt eller oläsbart pass. `recordStatus` är `current` (nuvarande personbästa), `second`/`third` (näst/tredje bästa tid hittills; lika tid behåller den äldre insatsen före), `previous` (tidigare personbästa utanför topp tre), `year`/`yearSecond`/`yearThird` (placering inom insatsens UTC-år för övriga) eller `null`. Flödet visar samma status. För andra än ägaren är status alltid `null`, så privata pass inte kan påverka synlig rekordstatus. |
| `GET /api/records` | Snabbaste insatsen per distans; vid lika tid väljs den tidigaste. |
| `GET /api/records?year=2026` | Samma urval inom UTC-kalenderåret då insatsen började. |
| `GET /api/records/trend?distanceM=5000&timeZone=Europe/Stockholm` | `months`: bästa insatsen per lokal kalendermånad (bara månader med insatser). `progression`: rekordhistoriken, varje insats som var strikt snabbare än alla tidigare; lika tid behåller det äldre rekordet. 400 för annan distans än etiketterna ovan eller ogiltig tidszon. |
| `GET /api/records/predictions` | Loppprognos: `basis` (underlagets insats), `vdot` och `predictions` med `distanceM`/`elapsedS` för alla rekorddistanser. Tomt underlag ger `null` och en tom lista. |
| `POST /api/activities/{id}/best-efforts/recalculate` | Ersätter passets insatser från sparade tidsserier; 204 vid framgång, annars 404 för okänt eller annans pass. Kräver `X-CSRF-TOKEN`. |
| `GET /api/activities/{id}/similar` | Ägarens pass på samma runda som passet (inklusive passet självt), nyast först; 404 för andras pass, även delade. Passsidan visar Samma runda med placering i tid när det finns minst ett annat pass, och en tidsgraf från tre. |
| `GET /api/routes` | Ägarens tio mest körda rundor (minst två gånger), flest först: sportgrupp, antal, total distans, bästa tid, senaste passet och referenslinjen förenklad som GeoJSON. Visas som Mina rundor på Statistik. Rundorna (`Routes.cs`, migrering 034) tilldelas vid läsning: varje pass med GPS-linje utan runda jämförs, äldsta först, mot rundornas referenslinjer i samma sportgrupp med ruttlängd inom 5 % och Fréchetavstånd högst 30 m (plus halva förtätningssteget, 20 m eller längden/500) i referensens UTM-zon, så en slinga åt andra hållet är en annan runda. Bästa matchningen vinner; utan matchning blir passets linje en ny runda. Ändras toleranserna påverkar det bara nya tilldelningar; för att gruppera om, nollställ `activities.route_id` och radera rundorna. Toleranserna ska kalibreras mot riktiga GPS-spår och återanvändas för segment. |

På passets redigeringssida kan ägaren välja **Undanta från löprekord** vid exempelvis GPS-glapp. `PUT /api/activities/{id}` sparar `excludeFromRecords` (boolesk, förval false; utelämnat fält behåller valet). Passdetaljer och export innehåller markeringen. Hela passet filtreras bort från personbästa, årsrekord, taggfiltrerade rekord, rekordutveckling och prognoser. Träningsstatistik och mål påverkas inte. Passets beräknade insatser behålls och visas fortfarande på passsidan; undantaget överlever omberäkning och redigering av sportdelar och kan ångras utan ny import.

Omberäkningen kan upprepas efter ändrad beräkningslogik och fungerar även utan
sparad råfil eller pulssamtycke. Kör den för befintliga pass som importerats innan
rekordmotorn infördes; de fylls inte automatiskt i vid migreringen. Pass utan
distansström och andra sporter ger en tom lista. Person- och årsbästa beräknas från
`best_efforts`, så borttagning av ett pass gör att nästa bästa insats tar över direkt.

Prognosen (`RacePredictor`) använder Jack Daniels och Jimmy Gilberts formler: syreupptag
vid farten och andel av maxkapaciteten under tiden ger VDOT, och samma VDOT ger
likvärdiga tider på andra distanser (bisektion över tiden). Underlaget är insatsen med
högst VDOT bland 5 km och längre de senaste 90 dagarna (lika VDOT: den senaste);
kortare insatser ur intervallpass överskattar uthålligheten. VDOT över 90, över
världsrekordnivå, behandlas som mätfel. Statistiksidan visar personbästa och årsbästa
per distans, ett diagram med månadsbästa och rekordtrappa, rekordhistoriken som tabell;
prognosen visas under Analys. Passidan visar en separat lista över personbästa som sattes på passet,
med en trofé i guld och medaljer i silver med metallisk glans för nuvarande respektive tidigare
rekord. Status visas i tooltips som öppnas med mus, tangentbord eller tryck. Samma märkning visas
vid bästa insatser; en ljusgrå rosett i kontur markerar årsbästa när insatsen inte är ett personbästa.
Personbästamärkningen visas också
vid passlänkarna i rekordhistoriken. Status följer insatsernas tidpunkt, oberoende av
importordning, och räknas från nu sparade insatser även efter radering eller omberäkning.

Beräkningstesterna jämför också med en uttömmande referensberäkning på oregelbundna
strömmar. Integrationstesterna använder syntetiska FIT-filer skapade med Garmins SDK
och kontrollerar import, omberäkning, årsskifte, kaskadradering, CSRF och ägarskap.

Aktiviteter kan ha en valfri beskrivning på högst 10 000 tecken. `description`
skickas i JSON för manuella pass och som formulärfält vid FIT-uppladdning.
Tom text sparas som null. Beskrivningen behålls när en FIT-fil väljs i formuläret
och visas som vanlig text med radbrytningar på passsidan. Migrering 012 lägger
till fältet; redan importerade dubbletter hoppas fortfarande över vid Strava-import.

## Exportera data

Öppna **Profil → Inställningar → Data och integritet → Exportera data → Ladda ner ZIP**. Låt sidan vara öppen medan arkivet förbereds. `GET /api/me/export` kräver inloggning och hämtar bara det egna kontots data, oberoende av filter och sidindelning i passlistan. Svaret är en nedladdningsbar `kuta-export.zip` med `Cache-Control: no-store`.

- `avatar.jpg`: profilbilden, om en finns.
- `profile.json`: id, e-postadress, visningsnamn, profilens skapandetid, tidpunkt för pulssamtycke och för godkända villkor.
- `equipment.json`: all egen utrustning, även pensionerad, med startvärden och total användning.
- `privacy-places.json`: egna dolda platser med namn, mittpunkt och radie. Listan ingår även utan pass.
- `goals.json`: egna träningsmål med sportgrupp, typ, period, mål i SI-enheter, upprepning, startdatum och synlighet.
- `achievements-seen.json`: vilka utmärkelser användaren har sett och när.
- `activities/<id>.json`: källa, skapandetid, synlighet, Strava-ID, sökväg till råfilen (eller `null`), `activity` med samma passdetaljer som passsidan och `bestEfforts` med passets bästa insatser. Detaljerna innehåller beskrivning, taggar, utrustnings-ID:n, tidsserier, varv samt originaldelar och redigerade multisportdelar.
- `raw/<id>.fit` eller `.gpx`: sparade råfiler med oförändrat innehåll. Manuella pass saknar råfil. Råfiler som inte sparats utan pulssamtycke, eller som raderats vid återkallat samtycke, kan inte exporteras. Gzip-filer från Strava har packats upp vid importen.

JSON använder lowerCamelCase, SI-enheter och UTC-tid. Lösenord, sessionscookies och tokens ingår inte. Ett konto utan pass får fortfarande profil, utrustningslista, dolda platser och mål. Arkivet byggs färdigt på en tillfällig fil före HTTP-svaret; den raderas vid fel, avbrott eller när överföringen är klar. Profil och pass låses mot skrivning under bygget för att samtyckesåterkallelse eller passradering inte ska kunna ta bort en råfil mitt i exporten. Om en förväntad råfil saknas misslyckas exporten i stället för att tyst lämna bort den.

Exportens innehåll, åtkomst mellan två användare, råfilernas bytes och samordningen med återkallat samtycke testas på databas-/lagringsnivå. HTTP-testet verifierar den nya routens autentisering, ZIP-svar och cache-/nedladdningsheaders; det upprepar inte innehållstesterna.

## Importera en Strava-export

Öppna **Profil → Inställningar → Import och anslutningar** via kugghjulet. Sidan visar stegen för att beställa arkivet hos Strava, med direktlänk. Under **Importera från Strava** väljer du Stravas kompletta ZIP-export (högst 500 MB) och trycker **Importera pass**. Ta ställning till pulssamtycket först. Uppladdningen köar importen, som sedan körs på servern (se [Jobbkö](#jobbkö)). Under importen visas antal bearbetade pass och löpande räknare för importerade, dubbletter och misslyckade. Sidan kan lämnas; förloppet och det senaste resultatet visas igen när du kommer tillbaka. Exporten måste innehålla `activities.csv` i roten och filerna på sökvägarna i kolumnen `Filename`.

Träningsformen bevaras även för exempelvis terränglöpning, elcykling och virtuella pass. Namn från Stravas CSV och kompakta sportnamn normaliseras vid importen; okända framtida namn sparas också. Äldre importer slog ihop Run/Trail Run/Virtual Run respektive Ride/Virtual Ride/E-Bike Ride. Dessa redan sparade pass får ikonen för sin sparade sport; återimport av dubbletter ändrar inte sporten. Rekord beräknas för löpning, terränglöpning och virtuell löpning.

Importen behåller passnamn, beskrivning (kolumnen `Activity Description`), taggarna `race` och `long_run` ur kolumnen `Tag` (Stravas övriga taggar som `running` och `commute` hoppas över), sport, starttid (UTC), distans i meter, tid och tillgängliga sammanfattningar från CSV. FIT och GPX, inklusive `.fit.gz` och `.gpx.gz`, ger tidsserier och rutter. Pass utan aktivitetsfil får en sammanfattning utan rutt. GPX-avstånd beräknas från GPS-punkterna; CSV-distansen behålls i sammanfattningen, så de kan skilja sig. Foton, kontakter, privata anteckningar och cyklar importeras inte. Detta använder användarens arkiv, utan anslutning till Stravas API.

Alla importerade pass är privata. Puls och de enskilda råfilerna sparas bara med samtycke; ZIP-arkivet sparas inte som råfil. Återkallat samtycke raderar puls och råfiler även från importerade pass. Importen fyller inte på puls i redan importerade pass: ta bort och importera dem igen om de först laddades upp utan samtycke.

Skor importeras före passen och kopplas med `Gear`-ID från `activities.csv`. Namn och märke hämtas från `shoes.csv` när dess namn kan matchas; annars används passets utrustningsnamn. Skor med olika Strava-ID hålls isär även om namnen är lika. `shoes.csv` saknar egna ID:n, så skor som inte används på något pass kan inte importeras säkert. Exporten saknar pensionsstatus: nya skor blir aktiva. Startdistans och starttid sätts till noll; statistik räknas från de kopplade passen, utan dubbelräkning. Redan importerade skors namn, startvärden och pensionering bevaras vid återimport. Återimport kompletterar tidigare pass som saknar utrustning, även dubbletter via filhash, men skriver inte över befintliga utrustningsval. Historiska pass får kopplas till pensionerade skor.

Efteråt visas antal importerade pass, skor, kompletterade skokopplingar och dubbletter samt Strava-ID för pass som misslyckats (till exempel en saknad, trasig eller ej stödd aktivitetsfil). Tidigare sparade pass behålls om ett senare pass misslyckas eller anslutningen bryts. Kör samma export igen för att fortsätta; Strava-ID och identiska aktivitetsfiler förhindrar dubbletter inom kontot. Passlistan visar 25 pass åt gången; resten nås med Visa fler.

API: `POST /api/activities/import/strava`, multipart-fält `file`, med inloggning och CSRF-token. Anropet sparar arkivet, kontrollerar manifestet och svarar `202 Accepted` med jobbet (`Location: /api/jobs/{id}`). Valideringsfel returneras som vanliga JSON-felsvar före köandet, och en redan pågående import ger `409` med `importInProgress`. Kroppen läses som ström först efter kontrollerna (inte som `IFormFile`, som buffrar hela arkivet innan handlern körs), under ett sessionslås per användare. Arkiven ligger på maskinens disk tills arbetaren kört dem, så med fyra köade eller pågående importer totalt, eller två samtidiga uppladdningar, svarar anropet `503` med `importBusy`. Jobbets `progress` är `{ processed, total, imported, duplicates, failed }` och `result` vid framgång `{ imported, duplicates, failedActivityIds, equipmentImported, equipmentLinked }`. Ett jobb vars arkiv försvunnit (omstart av maskinen) avslutas med `stravaImportInterrupted`; ladda upp samma fil igen, dubbletter hoppas över. Högst 20 000 pass, 100 000 ZIP-poster, 20 MB per uppackad fil och totalt 1 GiB uppackade aktivitetsfiler per anrop, inklusive lästa byte från filer som avvisas. Budgeten räknas under läsningen; när den tar slut hoppas resterande pass över och rapporteras som misslyckade. Enskilda fel hindrar inte andra pass; ett ogiltigt CSV-manifest avvisar hela arkivet före lagring.

## Garmin Connect

Anslutningen följer Garmin Connect Developer Program (OAuth2 PKCE, push). Dokumentationen är konfidentiell och ligger inte i repot.

- **Anslut:** Inställningar → Garmin Connect. `POST /api/garmin/connect` (CSRF) lägger PKCE-verifierare och state i en krypterad cookie (15 minuter, sökväg `/api/garmin/callback`) och returnerar Garmins godkännandeadress. Garmin skickar tillbaka webbläsaren till `GET /api/garmin/callback`, som byter koden mot tokens på servern, hämtar Garmins användar-id och behörigheter och skickar vidare till `/app/settings?garmin=connected|declined|failed|inUse|unavailable`. Ett Garminkonto kan bara vara kopplat till ett Kuta-konto.
- **Lagring:** `garmin_connections` (migrering 025) med tokens krypterade via Data Protection (`kuta.garmin-tokens.v1`). Access-token förnyas vid behov; arbetarens timvisa städning förnyar refresh-token (giltig tre månader) när mindre än 14 dagar återstår, så att avregistrering fungerar även utan ny data.
- **Push:** Konfigurera Activity Files, Deregistrations och User Permissions att skickas till `https://kutaapp.com/api/garmin/push` i Garmins API-verktyg. Endpointen svarar `200` när aviseringarna sparats i `garmin_files` och köar jobbet `garmin_import`. Avsändaren måste ligga i Garmins IP-nät (`Garmin:VerifyPushSource`, av i Development) och callback-adresser godtas bara på `https://apis.garmin.com`. Jobbet hämtar FIT-filen (en gång, inom 24 timmar), sparar passet med källan `garmin`, Garmins aktivitetsnamn, beskrivning och enhetsnamn, och följer pulssamtycket som annan import. Andra filtyper än FIT hoppas över. Starter Access ger bara nya pass, ingen historik.
- **Nya pass:** löppass från Garmin får senast använda aktiva sko, som i passformuläret. Garmin-pass sparas utan `reviewed_at` (migrering 026); Pass och Flöde visar ett kort med antal nya pass, länk till redigering av det senaste och "Markera som granskade", och passlistan visar etiketten Ny. Sparad redigering (`PUT /api/activities/{id}`) eller `POST /api/activities/unreviewed/dismiss` markerar passen som granskade; `GET /api/activities/unreviewed` ger `{ count, latest }` (id, titel, sport, starttid; ett enda pass visas som "Nytt pass från Garmin: titel" med Redigera och Ok, då vet jag).
- **Synlighet:** Garmin-pass är alltid privata (constraint `activities_garmin_private`) tills Garmin godkänt visningen för andra.
- **Koppla från:** `DELETE /api/garmin` och Garmins avregistrering raderar råfiler, Garmin-pass, väntande filer och anslutningen. Kuta anropar Garmins `DELETE /user/registration` vid frånkoppling och kontoradering. `GET /api/garmin` ger `{ available, connected, activityExport, connectedAt }`. Exporten innehåller `garmin.json` utan tokens.
- **Drift:** Lägg fälten `Garmin__ClientId` och `Garmin__ClientSecret` i 1Password-objektet `fly-kuta-new` innan `scripts/fly-secrets.sh` körs (`fly.secrets.tpl` refererar till dem). Utan dem visar Inställningar att anslutningen inte är tillgänglig.
- **Spärr före lansering:** även med nycklar kan bara konton i `Garmin__AllowedUserIds` (kommaseparerade användar-id i `fly.toml` under `[env]`) ansluta. `Garmin__OpenToAll = 'true'` öppnar för alla; sätt det först när integritetspolicyn beskriver anslutningen som tillgänglig och Garmins 30-dagarsfrist gått ut. Befintliga anslutningar fortsätter ta emot data och kan alltid kopplas från. Fly sätter `Fly-Client-IP`, som IP-kontrollen kräver via `ForwardedHeaders`.

## Admin

`admin.kutaapp.com` visar aggregerade siffror (`GET /api/admin/stats`, `web/src/routes/admin.tsx`): konton, bekräftade konton, nya konton och inloggade de senaste 30 dagarna, konton med aktiviteter, aktiviteter och nya aktiviteter de senaste 30 dagarna. Inga namn, adresser eller andra uppgifter per konto, och inga åtgärder.

- **Eget värdnamn** (`Admin__Host`, upplägget kommer från Kredovis operatörsvy): samma app och databas, men `Admin.UseAdminHost` släpper bara igenom inloggning, `/api/me` och `/api/admin` där, och svarar 404 på `/api/admin` och `/app/admin` på alla andra värdnamn. Ett injicerat skript i appen på kutaapp.com kör alltså aldrig där en adminsession gäller; sessionscookien är en `__Host-`-cookie och delas inte mellan värdnamnen. `/` och `/app` på adminvärden skickar till `/app/admin`. Utan `Admin__Host` är vyn av utanför Development; lokalt nås den på `/app/admin`.
- **Administratörer** är kommaseparerade användar-id i `Admin__UserIds` (`fly.toml`, `[env]`), aldrig e-postadresser. Andra konton får 404.
- **Drift:** DNS-post för `admin` hos Cloudflare (bara DNS, som `kutaapp.com`), `fly certs add admin.kutaapp.com`, och värdnamnet i Turnstile-widgetens lista hos Cloudflare.
- **Saknas:** MFA (Kuta har ingen ännu). Den behövs innan vyn visar uppgifter om enskilda konton eller kan ändra något.

## Karta

Bakgrundskartan är en egen [PMTiles](https://docs.protomaps.com/pmtiles/)-fil, utklippt ur Protomaps dagliga bygge av OpenStreetMap (ODbL; attributionen visas i kartan), med Protomaps stil `light` (`@protomaps/basemaps`) och kartetiketter på appens språk. Webbläsaren läser filen med range-anrop via `pmtiles`-protokollet i `web/src/lib/map.ts`, så ingen kartleverantör ser vilket område som visas. Typsnitt och ikoner (Protomaps `basemaps-assets`) ligger bredvid filen.

- **Inställning:** `VITE_MAP_URL` (bas-URL) och `VITE_MAP_TILES` (filnamn) sätts i `web/vite.config.ts` (lokalt `/map-dev`, i bygget R2) och kan åsidosättas med miljövariabler eller `web/.env.[mode].local`. CSP:n i `vite.config.ts` tillåter originet i `VITE_MAP_URL` för `img-src` och `connect-src`; en relativ URL behöver inget tillägg.
- **Lokalt:** `scripts/fetch-map.sh` hämtar Uppsala–Stockholm (~100 MB) till `web/.map-dev` (ignoreras av git), som Vite servar under `/map-dev` med range-stöd i både dev och preview. Kräver `pmtiles` på PATH (`brew install pmtiles`, eller binären från [go-pmtiles releases](https://github.com/protomaps/go-pmtiles/releases)) eller Go. Utanför området blir kartan tom. För att prova ett bygge lokalt: lägg `VITE_MAP_URL=/map-dev` och `VITE_MAP_TILES=basemap.pmtiles` i `web/.env.production.local` och kör `npm run build && npx vite preview`.
- **Drift:** R2-bucket med jurisdiktionen EU (ingen platshint) och egen domän `tiles.kutaapp.com` (Cloudflare). EU-buckets nås via S3-endpointen `https://<konto-id>.eu.r2.cloudflarestorage.com` vid uppladdning. Filen och tillgångarna tas fram med `scripts/fetch-map.sh --region sweden --out DIR` (Sveriges omslutande rektangel till zoom 15, ~5 GB; `--region nordics` ger Norden med Island, Färöarna och Svalbard, ~10 GB) och laddas upp till bucketens rot med `scripts/upload-map.sh DIR`, som kör `rclone copy` med nyckeln från 1Password via `op run` (referenser i [scripts/map-upload.env.tpl](scripts/map-upload.env.tpl)) och sedan kontrollerar range-läsning via `tiles.kutaapp.com`. Kräver inloggad 1Password CLI och rclone. Objektet `kuta-map-r2` i valvet `Development` har fälten `account-id`, `bucket`, `access-key-id` och `secret-access-key`; nyckeln är en R2-token med bara Object Read & Write på kartbucketen och hålls skild från appens hemligheter i `fly-kuta-new`, eftersom appen inte behöver den. CORS-regel på bucketen: `AllowedOrigins` `https://kutaapp.com`, `AllowedMethods` `GET`/`HEAD`, `AllowedHeaders` `Range`, `ExposeHeaders` `ETag`, `Content-Range`, `Content-Length`. Kartfilens namn innehåller byggdatum, så ett nytt bygge laddas upp under nytt namn och standardvärdet för `VITE_MAP_TILES` i `web/vite.config.ts` byts; ta bort den gamla filen efter driftsättningen.
- **Cache:** `scripts/upload-map.sh` sätter `Cache-Control: public, max-age=31536000, immutable` på kartfilen (namnet innehåller byggdatum och ändras aldrig) och `public, max-age=2592000` (30 dagar) på `fonts/` och `sprites/`, så att webbläsaren inte hämtar dem igen. `--refresh-headers` skriver om redan uppladdade typsnitt och ikoner med headern; kartfilen får den vid nästa bygge. Cloudflares egen cache kräver en cacheregel (Caching → Cache Rules): värdnamnet `tiles.kutaapp.com`, *Eligible for cache*, Edge TTL och Browser TTL enligt ursprungets `Cache-Control`. Den täcker typsnitt och ikoner; kartfilen är större än Cloudflares gräns för cachade objekt, så dess range-anrop går alltid till R2.
- **Kostnad:** lagring i R2 ~0,015 USD/GB och månad (Sverige ~0,08 USD), ingen avgift för utgående trafik; varje kartbild är en läsning (de första 10 miljonerna per månad ingår).
- **Täckning:** utanför Sverige (och gränstrakterna i rektangeln) visas ingen bakgrundskarta, bara rutten. Utöka genom att ändra regionen i skriptet eller hämta hela jorden (~140 GB).

## Driftsättning (Fly.io)

En egen Fly-app (`kuta`, region `arn`). `Dockerfile` bygger webben och API:t till en image där API:t servar webben, så att de delar origin. Migreringarna körs när appen startar.

**Domän före deploy:** `CanonicalHost` i `fly.toml` är `kutaapp.com`. Konfigurera DNS för domänen mot Fly-appen och verifiera att Fly har ett giltigt TLS-certifikat för `kutaapp.com` innan du deployar denna konfiguration. Appen skickar vidare `kuta.fly.dev` och `www.kutaapp.com` till `https://kutaapp.com`.

**Supabase-projektet** (en gång, i dashboarden):
- EU-region. Stäng av Data API. Minsta lösenordslängd 8.
- Authentication → URL Configuration: Site URL `https://kutaapp.com` (bekräftelselänken byggs på den). Adressen ska följa `CanonicalHost` i `fly.toml` och vara inställd före deploy.
- Authentication → Email Templates → Confirm signup: innehållet i `supabase/templates/confirmation.html`.
- Authentication → Email Templates → Reset password: innehållet i `supabase/templates/recovery.html`. Krävs före användning av lösenordsåterställning i drift; standardmallen skickar sessionstokens till webbläsaren i stället för vårt API.
- Authentication → Attack Protection: **Captcha protection** på med Cloudflare Turnstile. Skapa en widget i Cloudflare → Turnstile (hostname `kutaapp.com`, läget Managed) och lägg in dess secret key. Samma widget används av Kutas formulär (se Turnstile nedan). Captchan stoppar direktanrop till Supabase Auth med den publika nyckeln, som annars går förbi API:ts gränser. API:t slipper den eftersom det anropar med `Supabase__SecretKey`; utan den hemligheten i drift slutar inloggning, registrering och återställning att fungera. Kontrollera med ett anrop till `/auth/v1/recover` med den publika nyckeln, som ska svara `captcha_failed`. Lokalt är captcha avstängd i `supabase/config.toml`.
- Authentication → Rate Limits: **IP Address Forwarding** på. Utan det ignorerar Supabase `Sb-Forwarded-For` och räknar alla API:ts anrop som en klient.
- Egen SMTP (Authentication → Emails → SMTP Settings): Scaleway TEM, host `smtp.tem.scaleway.com`, port 587, användare = Scaleway-projektets id, lösenord = secret key för en API-nyckel med bara TEM-behörighet (sparad i 1Password), avsändare på kutaapp.com. Domänen verifieras i Scaleway med SPF, DKIM och DMARC hos Cloudflare. Höj mejlgränsen under Authentication → Rate Limits. Supabases inbyggda mejl skickar bara till projektets egna medlemmar.

**Turnstile** i Kutas formulär för inloggning, registrering och glömt lösenord: använd samma widget som Supabases captcha (Cloudflare → Turnstile, hostname `kutaapp.com`, läget Managed). Lägg dess site key i `fly.toml` under `[env]` som `Turnstile__SiteKey` (publik) och dess secret key som fältet `Turnstile__SecretKey` i 1Password-objektet `fly-kuta-new` innan `scripts/fly-secrets.sh` körs. Kontrollen är på först när båda finns; `GET /api/turnstile` visar site key. Webben visar widgeten i de tre formulären, och API:t kontrollerar token mot Cloudflare innan Supabase anropas (`turnstileFailed` respektive `turnstileUnavailable` om Cloudflare inte svarar). Lokalt och i testerna är kontrollen av. Cloudflares testnycklar (site key `1x00000000000000000000AA`, secret `1x0000000000000000000000000000000AA`) går att sätta som miljövariabler för att prova widgeten mot den byggda webben.

**Hemligheter** ligger i 1Password, aldrig i filer eller kommandon. [fly.secrets.tpl](fly.secrets.tpl) listar dem som `op://`-referenser; värdena finns i objektet `fly-kuta-new` i valvet `Development`, ett fält per hemlighet med hemlighetens namn:

| Fält | Värde |
|---|---|
| `ConnectionStrings__Default` | Supabase → Connect → Session pooler, i Npgsql-format: `Host=…;Port=5432;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Maximum Pool Size=10` |
| `Supabase__SecretKey` | Supabase → Project Settings → API Keys → secret key (`sb_secret_…`) |
| `Turnstile__SecretKey` | Cloudflare → Turnstile → Kutas widget → secret key (se Turnstile ovan) |
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

**Automatisk deploy:** `.github/workflows/deploy.yml` kör frontendkontroller och
API-tester på exakt den commit som pushats till `main` (inklusive merge av en PR).
När båda lyckas bygger Fly samma commits `Dockerfile` och deployar till `kuta`
med `--remote-only --ha=false`. Flys hälsokontroll på `/api/health` kontrollerar
att appen kommer upp; migreringarna körs vid start. Ersätter flyctl en maskin i
stället för att uppdatera den kan den gamla bli kvar och startas igen med förra
releasen, så deployen förstör efteråt maskiner som ligger på en äldre release.
Efter en manuell deploy: kontrollera med `fly machines list` att bara en maskin
finns kvar. Bara en deploy körs åt gången,
en pågående deploy avbryts inte och commits som inte längre är toppen av `main`
hoppas över. Även en push med enbart dokumentation kör kontroller och deploy.

Engångsuppsättning för CI:

1. Skapa en appbegränsad Fly-deploytoken för `kuta`, med namn `github-actions-kuta`
   och exempelvis ett års giltighet (`fly tokens create deploy --app kuta
   --name github-actions-kuta --expiry 8760h`). För över värdet direkt till
   GitHub Actions utan att skriva det i filer, kommandon eller chatt.
2. Spara hela token som GitHub Actions-secret `FLY_API_TOKEN` för
   `simonauner/kuta`, antingen som repository-secret eller som environment-secret
   i `production`. CI behöver bara denna token; appens databas-, lagrings- och
   certifikathemligheter ligger kvar hos Fly och hanteras via 1Password som tidigare.
3. Begränsa environment `production` till grenen `main`. Lämna required reviewers
   avstängt för automatisk deploy utan manuellt godkännande. Skydda `main` mot
   direktpush om alla produktionsändringar ska gå via PR.

Rotera Fly-token före utgången genom att uppdatera GitHub-secreten. Den ges bara
till deploysteget; frontend- och API-jobben får inga produktionshemligheter.
Saknad eller utgången token stoppar deployen. Upplägget följer
[Flys CI-guide](https://fly.io/docs/launch/continuous-deployment-with-github-actions/).

**Manuell deploy:** kör en maskin tills migreringar och token-förnyelse klarar flera maskiner, enligt `AGENTS.md`.

```bash
fly deploy --ha=false
```

## Integritet: underlag och öppna frågor

Arbetsunderlag för `web/privacy.html`, kontrollerat mot `main` på `24df08e` och leverantörskällor 2026-11-01. Skilj uppgifter som Simon har bekräftat, kodgranskning, leverantörers dokumentation och återstående frågor. Policyn är fortfarande ett utkast; denna lista innebär inget godkännande av publicering eller driftändringar.

### Bekräftat och beslutat

- **Aktuellt driftprojekt:** `umjqrlkeuppyhxnlyuid`, skapat med `accounts@kutaapp.com`, har ersatt `hrkqzqfyisvdyzfxzokf` med tom installation enligt Simons beslut. Inga konton, pass eller filer kopierades. Det tidigare projektet och 1Password-objektet `fly-kuta` behålls; de ska inte raderas som del av bytet.
- **Nya projektets inställningar bekräftade av Simon:** Stockholm (`eu-north-1`), Data API av, databaslagring av Auth-auditloggar av, Site URL `https://kutaapp.com`, Custom SMTP via befintliga Scaleway TEM, Kutas bekräftelse- och återställningsmallar från `supabase/templates/` med ämnesrader enligt `supabase/config.toml`, Email provider och Confirm Email på, minsta lösenordslängd 8 samt privat bucket `fit-files`.
- **Verifierat efter bytet:** Fly-konfigurationen är validerad; en maskin körs i `arn` med samma image som före bytet (`registry.fly.io/kuta:deployment-01M69SNDKJZ4957VVBXMYV14AN`). Endast anslutningskonfiguration och hemligheter byttes; lokala policyändringar publicerades inte. Maskinens konfiguration pekar på nya Auth- och Storage-adresser. `/api/health` ger HTTP 200 med PostGIS 3.3 och `/api/me` ger 401 utan inloggning. Inloggningssidan laddas i riktig webbläsare.
- **Databas och filåtkomst:** skrivskyddad fråga via Npgsql visar 0 Auth-användare, 0 Auth-auditposter, 0 pass och 20 journalförda migreringar, inklusive 017. En syntetisk fil skrevs och lästes via Kutas befintliga `RawFileStore`, raderades sedan och gav därefter 404. Efter auth-testerna visar en skrivskyddad aggregatfråga att den enda Data Protection-nyckeln har `encryptedSecret` och `EncryptedData`; nyckelinnehåll visades inte. Kontrollprogrammet byggdes med .NET SDK 10.0.402 och appens pinnade Npgsql/AWSSDK-versioner. Fyra nya 1Password-fält är läsbara; Auth-servernyckeln och den publika nyckeln accepteras av nya projektet. Simon bekräftar genomfört användarflöde för registrering, svenskt bekräftelsemejl och inloggning via länken mot nya projektet. Simon bekräftar även att återställningsmejl, lösenordsbyte och inloggning med det nya lösenordet fungerar mot nya projektet. Ingen ny applikationskod byggdes/deployades; den befintliga imagen återanvändes.
- **Hemligheter i drift:** `fly.secrets.tpl` läser databasanslutning, Auth-servernyckel och S3-nyckelpar från `Development/fly-kuta-new`; nuvarande certifikatreferens följer `fly.secrets.tpl` i huvudgrenen (`Development/fly-kuta-new`). Hemligheterna importerades via `scripts/fly-secrets.sh` utan att skriva värden i filer eller argument. Simon bekräftar Free-planen även för den nya organisationen. Dashboardens Project access visar enligt Simons återgivning en enda organisationsmedlem: `accounts@kutaapp.com`, med rollen Owner. Simon bekräftar även att tvåfaktorsautentisering är aktiverad för Supabase-kontot. Simon bekräftar att det nya Supabase-kontot skapades och tjänstevillkoren accepterades för Hack ska du ha AB:s räkning, av honom som bolagets företrädare. Övriga leverantörsuppgifter bedöms separat.
- Personuppgiftsansvarig: **Hack ska du ha AB, 559369-2287**. Simon Aunér är kontaktperson.
- `privacy@kutaapp.com` använder **Tuta (Tutao GmbH, Hannover)** direkt via domänens MX-poster sedan november 2026. Publika DNS-svar 2026-11-08 verifierar MX `mail.tutanota.de`, en gemensam SPF-post för Tuta och Scaleway (`include:spf.tutanota.de include:_spf.tem.scaleway.com -all`), Tutas DKIM-poster `s1`/`s2._domainkey` och DMARC `p=quarantine; adkim=s`. Inga Proton-poster finns kvar i DNS. Simon bekräftar 2026-11-08: biträdesavtalet med Tuta är signerat för Hack ska du ha AB (se tabellen nedan), tvåfaktorsautentisering är aktiverad, bara han har åtkomst till inkorgen, det finns inga vidarebefordringsregler, återställningskoden ligger i bolagets 1Password-valv och mottagning och svar fungerar. Autentiseringsheaders (SPF/DKIM/DMARC) för svaren är inte separat redovisade. Med strikt DKIM-justering (`adkim=s`) och `p=quarantine` måste även Kutas kontomejl från Scaleway vara DKIM-signerade med `d=kutaapp.com`. Verifierat 2026-11-08 med ett lösenordsåterställningsmejl från drift till Gmail: `DKIM-Signature` med `d=kutaapp.com`, och Gmail rapporterar `dkim=pass`, `spf=pass` (`smtp.mailfrom=noreply@kutaapp.com`) och `dmarc=pass (p=QUARANTINE)`. Privata kontaktvärden sparas inte här. Scaleway skickar fortfarande kontomejl.
- Tidigare gick kontaktmejl via Cloudflare Email Routing till privat Gmail och därefter (oktober–november 2026) till Proton Mail for Business. Simon bekräftade att Gmail bara innehöll egna testmejl och att Cloudflare Email Routing är avstängd. Simon bekräftar 2026-11-08 att Proton-kontot är avslutat. Protons krypterade säkerhetskopior behålls enligt deras Mail-policy i upp till 30 dagar.
- För det tidigare projektet bekräftade Simon **Free-planen** och att **Write audit logs to the database** var avstängt under Authentication → Audit Logs. Simon har även kört `select count(*) as audit_rows from auth.audit_log_entries;` i Kuta-projektets SQL Editor och rapporterar **0 rader**. Det verifierar tabellens aktuella innehåll, inte leverantörens externa loggar eller kopior. Uppgifterna kommer från Simon; projektmetadata kunde inte läsas via anslutningen på grund av saknad behörighet.
- **Leverantörssupport:** Simon bekräftar 2026-11-01 att inga Kuta-användaruppgifter, aktivitetsfiler eller loggar har skickats till leverantörernas support, inte heller egna testuppgifter. Det finns därför inga kända sådana supportkopior att hantera nu. Bekräftelsen avser aktivt inskickat material, inte leverantörernas automatiska driftloggar eller interna åtkomst.
- Simon uppger att Kuta ännu inte har någon egen backup. Det fastställer inte vilka kopior leverantörerna behåller.
- Simon vill kunna genomföra **export och kontoradering inom sju dagar från begäran**. Det är målet för rutinen, inte en implementerad eller verifierad funktion. Kontoradering av autentisering, appdata och råfiler finns i Inställningar och är verifierad lokalt med syntetiska uppgifter. Export via Inställningar finns och Simon har genomfört nedladdning i drift. Den tillhandahållna ZIP-filen kontrollerades 2026-11-01: CRC utan fel, samtliga pass- och konto-JSON läsbara, alla förväntade kontodokument med, inga saknade eller överflödiga råfilsreferenser och samma ägare på samtliga pass. Inga dubblettsökvägar eller osäkra sökvägar hittades. Denna kontroll jämför inte varje råfils byte mot lagringen och verifierar inte leverantörernas loggar/kopior. Simon har även skapat ett separat testkonto med ett manuellt pass (1 km, 10 minuter), genomfört kontoraderingsflödet och rapporterar att ett nytt inloggningsförsök ger ”Fel e-post eller lösenord.” Detta verifierar nekad inloggning efter flödet; testkontots underliggande databasrader, råfiler och leverantörsposter har inte inspekterats genom detta användartest. Simon bekräftar att han därefter loggat in på sitt vanliga konto och att dess träningspass finns kvar. Skrivskyddad databaskontroll efter testet visar bara det kvarvarande kontot/profilen, oförändrat antal vanliga pass, inget pass med testtiteln och inga sessionsrader utan Auth-användare. Detta användartest omfattade ett manuellt pass; radering av råfiler verifierades inte genom det. Arbetsrutinen för leverantörsposter finns nedan; exakta begränsningar och leverantörsbekräftelser återstår. Policyn får inte lova att alla leverantörsloggar och kopior försvinner inom samma tid.
- **Bevakning av integritetsärenden:** Simon bekräftar 2026-11-01 att han läser `privacy@kutaapp.com` varje dag, även under semester. Detta är den löpande bevakningen av nya ärenden; den månatliga genomgången nedan gäller gallring av avslutade ärenden.
- **Beslutad rutin för supportmejl:** avslutade ärenden raderas senast 12 månader efter avslut. Simon går igenom inkorgen varje månad och raderar även ärenden som annars når tolvmånadersgränsen före nästa genomgång. Papperskorgen töms för raderade ärenden. Onödiga känsliga uppgifter och bilagor raderas tidigare. Uppgifter som behöver bevaras för en konkret rättslig skyldighet eller tvist hanteras separat. Rutinen är beslutad av Simon. Simon bekräftar genomfört raderingstest med syntetiskt mejl: radering från inkorg, permanent radering av testmejlet i papperskorgen och kontroll genom sökning. Den löpande månatliga genomgången är en manuell rutin. Simon bekräftar 2026-11-01 att bara vanlig inkorg och papperskorg används, utan särskilda bevaranderegler i organisationen. Rutinen gäller nu Tuta-inkorgen. Simon bekräftar 2026-11-08 ett raderingstest i Tuta: ett mejl raderades och går därefter inte att hitta via sökning. Leverantörens interna kopior omfattas inte av inkorgens raderingslöfte.
- **Beslutad sessionslagring:** 30 dagar från det första av registrerat avslut (`ended_at`) och absolut utgång (`expires_at`). Migrering 019 skapar ett timvis Postgres-jobb och index för denna gräns. Lokal verifiering är genomförd: gränsfall för avslut/utgång, aktiva sessioner och utloggning i webbläsaren. Efter förening med aktuell huvudgren klarade samtliga 219 API-tester körningen (`KUTA_PORTS=58500 dotnet test --project api/Kuta.Api.Tests -- --parallel none`). Webbbygget klarade Node 24.22.1; API-testerna kördes med .NET 10.0.402 mot egen lokal Supabase på portserie 58500. Migrering 019 är aktiverad i nya driftprojektet. En återläsning verifierade aktivt schema `0 * * * *`; jobbets raderingskommando kördes även direkt utan fel och tog bort 0 rader. Skrivskyddad kontroll 2026-11-01 visar två lyckade schemakörningar; senaste observerade slut är 2026-11-01 00:00:00 UTC (01:00 svensk tid). Ingen ny appimage publicerades. Cookiens 30 dagars inaktivitet sätter inte i sig `ended_at`; utan tidigare registrerat avslut är den yttersta gränsen 180 + 30 dagar från inloggning, följt av nästa städning. Jobbet körs när databasen är i drift; driftavbrott eller ett pausat Supabase-projekt kan fördröja rensningen ([Supabase om pausning av Free-projekt](https://supabase.com/docs/guides/deployment/going-into-prod#availability)).

### Vad koden visar om loggar och sessioner

Granskat även: `JobQueue.cs`, `JobWorker.cs`, `DataExport.cs`, `MeEndpoints.cs` och migrering 020.

Granskat: `api/Kuta.Api/Program.cs`, `PrivateLogProvider.cs`, `LogException.cs`, `appsettings.json`, `SupabaseAuthClient.cs`, `SupabaseCookieSession.cs`, `ActivityStorage.cs`, `RawFileStore.cs`, auth-/aktivitetsendpoints och `web/src/lib/api.ts`.

- Kutas applikationsloggning är begränsad till granskade strukturerade driftfält och rensad undantagsmetadata; se [Applikationsloggar och integritet](#applikationsloggar-och-integritet) för tillåtna fält och testade gränser. Automatisk HTTP-klientloggning och DbUp:s konsolloggning släpps inte igenom. Ingen generell HTTP-body-loggning eller extern felrapportering är konfigurerad.
- Flys körande image verifierades skrivskyddat 2026-11-01 mot `GH_SHA=d35ad5387698b740bf598e010d8b5b2ff974535f`, med en maskin i `arn` utan ansluten volym eller loggningsöverstyrningar i den lästa konfigurationen. Den innehåller den granskade loggprovidern och jobbkön. Faktiskt innehåll i historiska plattformsloggar har inte inspekterats; kodens filtrering styr inte leverantörens egna loggar.
- **Bekräftat av Simon 2026-11-01:** Captcha protection (Cloudflare Turnstile) och IP Address Forwarding är på i Supabase Auth. Direktanrop med den publika nyckeln avvisas med `captcha_failed`, och inloggning, registrering och återställning i appen fungerar. Supabases captcha i sig visar ingen widget; Supabase får inga captcha-token från användare. Turnstile-widgeten i Kutas inloggnings-, registrerings- och återställningsformulär (när `Turnstile__SiteKey`/`Turnstile__SecretKey` är satta) skickar däremot IP-adress och webbläsaruppgifter till Cloudflare; det står i `web/privacy.html` avsnitt 5. Kontrollera Cloudflares DPF-certifiering och att DPA:n gäller Kutas Cloudflare-konto innan nycklarna sätts i drift.
- Besökarens IP skickas avsiktligt till Supabase Auth med `Sb-Forwarded-For` för rate limiting. Supabases egna auth-loggar kan innehålla användar-id, IP, user-agent, tidpunkt och händelse; de är separata från Kutas loggning ([Auth Audit Logs](https://supabase.com/docs/guides/auth/audit-logs)).
- `user_sessions` sparar användar-id, slumpat sessions-id, skapandetid, utgångstid och avslutstid. Utloggning sätter `ended_at`. Migrering 019 rensar timvis rader 30 dagar efter första registrerade avslut eller absoluta utgång; se den verifierade sessionsrutinen ovan. Cookiens inaktivitetsgräns sätter inte automatiskt avslutstid i databasen.
- Filnycklar innehåller användar-id och filhash. Sådana identifierare kan finnas i leverantörernas lagringsloggar även utan att själva filinnehållet loggas.

### Avtalsunderlag från Kredovis

Simon bekräftar samma leverantörer och avtal för Kuta. Underlaget är Kredovis `docs/privacy.md`, avsnitt **DPA checklist**, och `docs/transfer-assessment.md` i projektet `bokforing`. Det är dokumentation av tidigare operatörsbekräftelser och avtalsgranskning; signerade original öppnades inte i Kuta-genomgången. Den sparade Supabase-standardtexten har därefter textgranskats separat enligt tabellen nedan. Kredovis särskilda systemskydd och riskbedömningar ska inte automatiskt tillskrivas Kuta.

| Leverantör | Dokumenterat underlag | Bedömning för Kuta |
| --- | --- | --- |
| Supabase | Sparad PDF `Supabase DPA 2026-10-12pdf.pdf` textgranskad i Kuta-genomgången: utskrift av standard-DPA v1, 2026-08-01, från den officiella webbplatsen. Inledningen kopplar DPA:n till tjänsteavtalet; Schedule 3 (PDF-sida 18) identifierar kunden genom Supabase-kontot/avtalet. PDF:n anger inte Hack ska du ha AB, Kuta eller någon organisation. Simon uppger att Free-organisationen saknar bolagsuppgifter i faktureringen. Supabases [tjänstevillkor](https://supabase.com/terms), kontrollerade 2026-10-31, medger att villkoren ingås för en organisation av en behörig företrädare; faktureringsfält är inte i sig ett krav i den granskade DPA-texten. Schedule 3 omfattar hälsodata som möjlig känslig kategori; avsnitt 4 kräver uttryckligt samtycke. SCC:er anger Supabase Pte. Ltd i Singapore som importör. | Simon bekräftar att det nya kontot skapades och tjänstevillkoren accepterades för Hack ska du ha AB:s räkning, som bolagets företrädare. Partsanknytningen dokumenteras genom hans bekräftelse tillsammans med konto- och organisationsuppgifterna; inga bolagsfält i Free-planens fakturering krävs som ytterligare arbetssteg. Kutas ändamål, pulsdata och skydd beskrivs i policyn. Free-plan, extern loggtid och aktuell auditinställning är dokumenterade nedan. Överföringsunderlaget kompletteras enligt den riktade bedömningen nedan; frågor om interna kopior följs upp när de behövs för en konkret bedömning eller begäran. Anslutningen fungerade vid ny kontroll 2026-11-01: rätt projekt och region, 0 Auth-auditposter och privat `fit-files` verifierades skrivskyddat. |
| Fly.io | Ny standard-DPA för Hack ska du ha AB signerad 2026-10-31 via Dropbox Sign, sparad som `Fly 2026-10-31.pdf`. Bolag och signering på sida 11, fullständiga kundtexter om både Kredovis och Kuta på sida 24 samt slutförd signeringslogg på sida 28 är text- och visuellt granskade. Bilagan beskriver Kutas avsiktliga pulsbehandling, samtycke före lagring, skyddsåtgärder, tillfälliga filer på Fly och bestående lagring i Supabase. Frekvensen är kontinuerlig för båda produkterna. Tidigare signerad kopia bevaras. | Kända överföringar och leverantörsloggar beskrivs i policyn; aktiv DPF-certifiering och driftkonfiguration är kontrollerade. DPA-signeringen är en del av underlaget tillsammans med dessa kontroller. Inget separat avtalsutkast eller mejl krävs som arbetssteg efter den bekräftade signeringen i Dropbox Sign. |
| Scaleway | Bolagskonto, DPA, TEM-villkor och prenumeration på underbiträdesändringar bekräftade 2026-10-13. EU/Paris enligt leverantörens underlag. | Kutas kontomejl, publicerade lagringstider och supportvägen för enskilda mejlposter är dokumenterade. Använd den vägen när en relevant begäran kräver det. Kredovis använder även AI-tjänster; det gör inte Kuta. |
| Tuta | Biträdesavtal (Order processing agreement) mellan Hack ska du ha AB och Tutao GmbH, signerat 2026-11-08 i Tutas admin och sparat som PDF; textgranskat 2026-11-08. Avsnitt 2: behandlingen sker uteslutande inom EU/EES och överföring utanför kräver bolagets förhandsgodkännande; mejlserverloggar (avsändare, mottagare, tid, inga kund-IP) högst 30 dagar; IP-adresser lagras bara anonymiserade. Kortbetalningar går via Braintree (USA) med standardavtalsklausuler, vilket bara rör bolagets betaluppgifter. Avsnitt 6: nya underbiträden kräver förhandsbesked och möjlighet att invända. Avsnitt 10: data lämnas tillbaka eller förstörs vid avtalets slut; säkerhetskopior får skapas för ordnad drift. Bilaga 1: datacenter i Tyskland (ISO 27001), krypterade diskar, loggar över överföringar och ändringar sparas sex månader. | Tvåfaktorsautentisering, ensam åtkomst, inga vidarebefordringsregler och återställningskod i 1Password är bekräftade av Simon. Följ upp metadata-/åtkomstfrågor vid konkret behov enligt den proportionerliga bedömningen. |
| Cloudflare | Hanterar DNS. Tidigare Email Routing ingår inte längre i den verifierade inkommande mejlvägen. Kredovis dokumenterar bolagets self-servicekonto; DPA ingår i self-serviceavtalet ([Cloudflare](https://www.cloudflare.com/trust-hub/gdpr/)). | Simon bekräftar att Email Routing är avstängd. Simon bekräftar att Cloudflare-kontot som hanterar Kutas DNS används för Hack ska du ha AB:s räkning, som bolagets företrädare. Historiska routningsloggar är inte inspekterade; Simon uppger att bara testmejl skickats. |

### Överföringsunderlag för Kuta

Granskat 2026-11-01. Detta dokumenterar avtalsmekanismer och kvarstående risker, inte ett generellt godkännande av alla leverantörsöverföringar.

- **Fly.io:** [DPF-registret, deltagare 9027](https://www.dataprivacyframework.gov/participant/9027), kontrollerat i webbläsare 2026-11-01, visar aktiv EU–USA-certifiering för HR- och övriga uppgifter med nästa certifieringsdatum 2027-06-15. Adekvansbeslutet är grunden för överföringar som omfattas; den signerade DPA:ns Annex 2 innehåller SCC för övriga tillämpliga överföringar. Appmaskinen är i Stockholm. Enligt [säkerhetsinformationen](https://fly.io/legal/security-questionnaire/) behandlas plattformsloggar och driftmetadata i USA; TLS kan avslutas globalt och behörig personal kan arbeta globalt. Detta ska framgå för användaren, inte beskrivas som exklusiv EU-behandling.
- **Supabase:** [DPA](https://supabase.com/legal/customer-resources/data-processing-addendum), avsnitt 12 och Schedule 2, införlivar SCC; modul 2 gäller när bolaget är ansvarig och Supabase biträde. Avtalsparten är Supabase Pte. Ltd i Singapore. Regionvalet ger primär lagring/behandling i Stockholm med avtalets undantag, inte exklusiv EU-behandling. SCC räcker inte som enda riskbedömning av tillgång till läsbara hälso- och platsuppgifter; relevanta underbiträden, fjärråtkomst och skydd ska bedömas för Kuta. Den aktuella [underbiträdeslistan 2026-10-12](https://supabase.com/legal/subprocessor-list/October-12-2026.pdf) är granskad: bland annat amerikanska support-, hosting- och övervakningsaktörer. Listans land avser den listade aktören och bevisar inte att Kutas lagringsregion har ändrats. Alla listade ändamål/tjänster ska inte antas vara aktiva för Kuta. Simon bekräftar 2026-11-01 att prenumeration på underbiträdesändringar är genomförd med `accounts@kutaapp.com`.
- **Tuta:** Tutao GmbH och all lagring finns i Tyskland enligt [integritetspolicyn](https://tuta.com/privacy-policy) (version 2025-11-13, kontrollerad 2026-11-08) och biträdesavtalet, som binder behandlingen till EU/EES. Själva inkorgen kräver därmed ingen överföring till tredjeland. Braintree (USA) behandlar bara bolagets egna kortuppgifter.
- **Cloudflare:** [DPA avsnitt 6](https://www.cloudflare.com/cloudflare-customer-dpa/) anger DPF och SCC med modul 2 för ansvarig till biträde där SCC behövs. Publika A-svar för både rot och www pekade vid kontroll direkt på Fly; webbsvaret hade Fly-headers. Detta stöder DNS-rollen och visar inte Cloudflare som HTTP-proxy vid kontrollen. Email Routing är avstängd enligt Simon. DPA är inte i sig bevis för att alla DNS-/kontologgar stannar i EU.
- **Kutas kompletterande skydd:** privata pass som förval, separat pulssamtycke, begränsad delning, MFA för Tuta- och Supabase-kontona enligt Simons bekräftelser och borttagning/export. HTTPS skyddar transporten men gör inte behandlingen oläsbar för Fly/Supabase. Appservern behöver kunna läsa puls/rutt och hanterar tillfälliga filer. Lova därför inte klientstyrd totalsträckskryptering eller att leverantörer tekniskt saknar åtkomst.

### Leverantörernas lagring: känt och okänt

- **Fly.io:** applikationsloggar sparas sju dagar enligt [Logging overview](https://docs.fly.io/monitoring/logging-overview). [Säkerhetsinformationen](https://fly.io/legal/security-questionnaire/) anger proxyfelloggar med app-id, värd och sökväg, inga sparade inkommande klient-IP i logglagret, driftmått cirka 15 dagar samt separat säkerhetstelemetri med längre, ospecificerad lagring. För Kutas användares rättighetsbegäran används `compliance@fly.io`. Kuta har ingen ansluten Fly-volym; volymsnapshots är därför inte en aktiv Kuta-backup. Tiderna 30 dagar efter tjänstens avslut och högst 90 dagar för restbackuper avser tjänstens upphörande, inte radering av ett Kuta-konto. Intern AI-bearbetning kan omfatta plattformsloggar/support, men enligt Fly inte requestinnehåll eller träning av modeller.
- **Scaleway TEM:** enligt [dataskyddssidan](https://www.scaleway.com/en/docs/transactional-email/reference-content/data-privacy/) raderas normalt mejlinnehåll när mottagarservern accepterar mejlet, vid permanent leveransfel eller efter 36 timmars misslyckade försök. Undantag: spam 72 timmar och blockerad domän 15 dagar; krypterade säkerhetskopior sju dagar. Avsändare, mottagare, ämne, avsändande IP, leveransstatus och tidsstämplar kan finnas i metadata/loggar. De behålls medan TEM-domänen är aktiv. Enskilda poster/adresser/loggar kan begäras raderade via support; ingen tidsfrist för sådan radering har verifierats.
- **Supabase:** Simon bekräftar Free-planen även för den nya organisationen (`dircayqkwqajmxdbyina`, projekt `umjqrlkeuppyhxnlyuid`). [Prislistan](https://supabase.com/pricing), kontrollerad 2026-10-31, anger ett dygns lagring för API- och databasloggar och att automatiska backuper/PITR inte ingår i Free. Detta bevisar inte att interna driftkopior saknas. [Backupdokumentationen](https://supabase.com/docs/guides/platform/backups) beskriver dagliga backuper för betalplaner; databasbackuper inkluderar inte Storage-objekt. Databaslagring av Auth-auditloggar är avstängd enligt Simon och en läsning i SQL Editor visar 0 rader i `auth.audit_log_entries` enligt hans återrapportering. Externa Auth-loggar och interna leverantörskopior är separata från denna tabell.
- **Tuta:** enligt [integritetspolicyn](https://tuta.com/privacy-policy), kontrollerad 2026-11-08, lagras all data i ISO 27001-certifierade datacenter i Tyskland. Mejlserverloggar med avsändare, mottagare och anslutningstid (utan kunders IP-adresser) sparas i högst sju dagar enligt policyn men högst 30 dagar enligt biträdesavtalet; policytexten för Kuta använder avtalets 30 dagar. IP-adresser sparas bara anonymiserade för missbruksbekämpning, utan angiven tid. Personuppgifter raderas senast 30 dagar efter att avtalet upphört, med undantag för bokförings- och reklamationsuppgifter. Policyn anger ingen lagringstid för säkerhetskopior. Mejlinnehåll och ämnesrad är krypterade; avsändare och mottagare är det inte. Lova inte Sverige-exklusiv behandling.

**Utkastets status:** svenska och engelska policyn är anpassade till `main` på `24df08e`: supporterstatus och badgens synlighet, träningsmål och beräkningar, köade importer, tillfälliga filer, jobblagring, kartleverantörens loggar och verifierad Fly-certifiering. Rättighetsrutinen är dokumenterad nedan; sjudagarsmålet är internt. Utkastmarkeringen behålls inför slutgranskningen. Den kvarstående sakfrågan är underlaget för Kutas bedömning av Supabases överföringsskydd; separata svar från fyra leverantörer är inte ett publiceringskrav. Inga leverantörsmeddelanden har skickats och inget har publicerats.

**Webbverifiering 2026-11-01:** `npm run verify` passerar med Node 24.22.1 på underlaget från `main` på `24df08e`: formatering, lint, 70 tester och produktionsbygge. Svenska och engelska, rendering och språkankaret har kontrollerats i riktig webbläsare på egen Vite-förhandsvisning (58801), utan registrerade konsolfel. Ingen driftsättning ingår i verifieringen.

**API-verifiering 2026-11-01:** samtliga 225 tester passerar med .NET SDK 10.0.402 mot underlaget från `main` på `24df08e` och syntetiska uppgifter mot egen lokal Supabase på portserie 58800 (`KUTA_PORTS=58800 dotnet test --project api/Kuta.Api.Tests -- --parallel none`). Exporttesterna kontrollerar kontoavgränsning och originalfilernas bytes. Kontoraderingstestet kontrollerar autentisering, profil, utrustning, pass, puls, råfiler, tidsserier, varv, delar, rekord och sessioner samt bevarad åtkomst/data för en annan användare. Jobbtesterna täcker kontokaskad och lagringsgräns, och importtestet kontrollerar arkivradering efter jobb. Detta är lokal teknisk verifiering, inte ett test av leverantörernas interna kopior. Första försöket misslyckades eftersom testinstansens Docker-hälsokontroller löpte ut och databasen stoppades. En minskad egen instans med bara nödvändiga tjänster startades och kontrollerades frisk före den godkända körningen; inga lånade miljöer ändrades.

### Proportionerlig leverantörsbedömning

Arbetsbedömning för Kutas nuvarande behandling, 2026-11-01: använd redan granskade avtal, publicerade uppgifter och verifierade inställningar. Komplettera när en konkret lucka påverkar skyddet, informationen till användaren eller möjligheten att hantera en rättighetsbegäran. Det krävs inte ett särskilt mejlsvar om varje intern loggtyp eller en generell bekräftelse på radering av hypotetiska kopior. Detta ändrar granskningsomfattningen; okända tider blir inte därmed verifierade.

[GDPR artikel 13.2 a](https://eur-lex.europa.eu/eli/reg/2016/679) medger lagringstid eller, när tiden inte kan anges, kriterier för att bestämma den. Kriterier måste vara konkreta och motsvara faktisk behandling; en allmän formulering om att spara så länge det behövs ersätter inte underlag. [EDPB:s yttrande 22/2024](https://www.edpb.europa.eu/system/files/2024-10/edpb_opinion_202422_relianceonprocessors-sub-processors_en.pdf), särskilt sammanfattningen och avsnitt 2.1.2, tillåter att ansvarig bygger på biträdets information och anpassar kontrollens omfattning till riskerna. Ansvar, rättigheter och skyldigheten att bedöma överföringsskydd kvarstår. Egna gallringsregler gäller det Kuta styr; policyn ändrar inte ensidigt leverantörernas avtal eller lagring.

| Leverantör | Underlag för nuvarande användning | När en riktad komplettering behövs |
| --- | --- | --- |
| Tuta | Signerat biträdesavtal, lagring i Tyskland och loggtider enligt avtal och policy, MFA, ensam åtkomst och inga vidarebefordringsregler enligt Simon. Raderingstest i Tuta genomfört enligt Simon 2026-11-08. | När en rättighetsbegäran gäller identifierbara metadata utanför inkorgen och dokumentationen inte räcker, eller när behandling/avtal ändras. Kontakt enligt avtalets avsnitt 5. Ingen generell enkät behövs nu. |
| Fly.io | Signerad DPA, verifierad aktiv DPF-certifiering, publicerade loggtider och åtkomstbeskrivning samt Kutas granskade loggfiltrering. Global trafikhantering och USA-behandling redovisas; Kuta använder inte Fly-volymer. | Vid belägg för känsliga uppgifter i plattformsloggar, en incident, en relevant avtalsändring eller en rättighetsbegäran som kräver uppgifter/åtgärder utöver tillgängligt underlag. Kontakt: `compliance@fly.io`. Den separata säkerhetstelemetrins exakta tider är fortfarande inte fastställda. |
| Scaleway TEM | Bolagsavtal, publicerade tider för mejlinnehåll/kopior samt kriteriet aktiv mejldomän för leveransmetadata. Supportvägen för radering av enskilda mottagarposter är dokumenterad. | Använd supportvägen när en relevant begäran gäller kvarvarande mottagarposter och följ upp åtgärden. En garanterad leverantörsfrist behöver inte förhandlas fram i förväg; Kuta ansvarar fortfarande för sin egen hantering och återkoppling enligt GDPR. |

Dessa kompletteringar är behovsstyrd uppföljning, inte automatiska villkor för att färdigställa policytexten. Ny information om brister ska däremot bedömas när den kommer, inte skjutas upp till nästa användarärende. Inga meddelanden har skickats.

**Supabase — kvarstående riktad bedömning.** [Aktuell FAQ](https://supabase.com/legal/privacy-resources/data-residency-and-transfers-faq), DPA och underbiträdeslista ger stöd för region, SCC och åtkomstkontroller. Den [offentliga överföringsbedömningen från 2025-03-14](https://supabase.com/downloads/docs/Supabase%2BTIA%2B250314.pdf) föregår aktuell avtals-/driftinformation. Kuta behandlar läsbara hälso- och exakta platsuppgifter; därför behövs tillräckligt aktuellt underlag om myndighetsåtkomst och vidareöverföringar för att slutföra den egna bedömningen. Det måste inte heta TIA eller innebära en ny fullständig revision. Aktuell TIA från [Trust Center](https://trust.supabase.io/documents) är den föreslagna vägen, men likvärdigt underlag kan räcka. Att dokumentet har mottagits innebär inte i sig att skyddet är bedömt.

Förfrågan är förberedd men **inte skickad**. Till `privacy@supabase.io` ([angiven kontakt](https://supabase.com/legal/privacy-resources/data-residency-and-transfers-faq)). Ämne: **Kuta — current transfer safeguards assessment**.

> Hack ska du ha AB operates Kuta (kutaapp.com), a running app using Supabase Free in Stockholm for PostgreSQL, Auth and private Storage. Data includes account details, precise activity routes and heart rate stored with explicit consent. We have reviewed your DPA, subprocessor list and Data Residency and Transfers FAQ. Please provide your current Transfer Impact Assessment, or equivalent current documentation, addressing Singapore/US government access, onward transfers and supplementary safeguards applicable to this setup. We need it to complete our assessment for health and precise location data. No user data is attached and no deletion is requested.

När underlaget kommer: jämför med aktuell behandling och befintliga skydd, dokumentera slutsatsen och eventuella nödvändiga åtgärder. Fråga vidare endast om en väsentlig lucka återstår. Exakta tider för varje intern Supabase-logg ingår inte i denna generella begäran; relevanta lagringsfrågor hanteras enligt samma behovsprincip som för övriga leverantörer.

Kartbilderna kommer från Kutas egen kartfil i Cloudflare R2 (se Karta); ingen separat kartleverantör anlitas.

### Rättighetsbegäranden: arbetsrutin

Dokumenterad 2026-11-01 utifrån Simons beslut att färdigställa rutinen. Daglig bevakning, även under semester, är bekräftad av Simon. Verifieringen av de tekniska delarna redovisas ovan. För åtgärder som behöver utföras av en leverantör följs resultatet upp i det berörda ärendet. Självservice för export och kontoradering finns redan. Sju dagar är internt mål för export och kontoradering, inte ett löfte om radering av samtliga leverantörskopior.

1. Simon läser kontaktinkorgen varje dag, även under semester, och noterar mottagningsdatum, vad personen begär och sista svarsdag. En begäran som kommer till en annan Kuta-adress hanteras också; kräv inte ett nytt mejl för att börja hanteringen.
2. Använd inloggad självservice där den passar och hjälp användaren om den inte räcker. Vid manuell hantering ska kontokoppling och identitet kontrolleras proportionerligt innan uppgifter lämnas ut eller raderas. En avsändaradress ensam är inte tillräcklig för ett känsligt utlämnande; begär inte rutinmässigt legitimation, lösenord eller inloggningskoder.
3. Utred begärans omfattning: appdata, kontaktmejl, eventuella lokala arbetskopior och identifierbara leverantörsposter. ZIP-exporten av appdata är inte i sig en fullständig inventering av all personuppgiftsbehandling. Leverera i första hand export genom den inloggade appen; om det inte fungerar behöver en säker alternativ leverans ordnas för det enskilda ärendet.
4. Utför den tillämpliga åtgärden och kontrollera resultatet. För radering omfattar det egna pass, puls, råfiler, profil, utrustning, mål, dolda platser, relationer, sessioner och kontokopplade jobb. Säkerställ att andra konton inte påverkas. Bedöm också kontaktmejl och lokala kopior; rutinen om 12 månaders supportlagring innebär inte automatiskt avslag på en tidigare raderingsbegäran.
5. För relevanta leverantörsposter som appen inte styr: använd befintligt underlag för att bedöma om begäran kräver ytterligare uppgifter eller åtgärder. Kontakta berörd leverantör när det behövs, inte rutinmässigt alla leverantörer vid varje kontoradering. Skicka bara nödvändiga identifierare, inte aktivitetsfiler eller exporter. Följ upp begärda åtgärder och dokumentera resultat, undantag och tillämplig lagringstid eller kriterier. En skickad begäran är inte en bekräftad radering. Det finns enligt Simon inga tidigare inskickade supportkopior; detta utesluter inte automatiska driftloggar.
6. Återkoppla vad som har gjorts och eventuella begränsningar. Hantera begäran utan onödigt dröjsmål, normalt inom en månad. Vid tillåten förlängning informeras personen inom den första månaden om skälen och tidsplanen. Spara endast den ärendedokumentation som behövs och tillämpa supportärendenas beslutade lagringsrutin, med separat bedömning av konkreta rättsliga skyldigheter eller tvister.

Tidsfrister och proportionerlig identifiering bygger på [IMY:s vägledning om registrerades rättigheter](https://www.imy.se/verksamhet/dataskydd/det-har-galler-enligt-gdpr/de-registrerades-rattigheter/), kontrollerad 2026-11-01. Genomgången är inte ett genomförandetest eller en verifiering av leverantörernas interna radering.

### Status inför slutgranskning

- **Klart:** bekräftad bolags-/avtalsanknytning, daglig bevakning, beslutad mejlgallring, sessionsrensning, dokumenterad rättighetsrutin, verifierad export/kontoradering samt kända leverantörsroller och lagringstider. Underlag och avgränsningar finns ovan; de behöver inte bekräftas på nytt.
- **Kvar:** komplettera och dokumentera Kutas bedömning av Supabases överföringsskydd med aktuellt tillräckligt underlag. Granska därefter policytexten mot slutsatsen och ta bort utkastmarkeringen när slutgranskningen är klar. Inga svar från Tuta, Fly eller Scaleway krävs rutinmässigt för detta.
- **Löpande:** följ relevanta avtals-/underbiträdesändringar och hantera konkreta rättighetsärenden enligt rutinen. Uppdatera information och bedömning före Garmininsamling eller annan väsentligt ändrad behandling. En faktisk brist kräver åtgärd; den kan inte lösas enbart med en bredare formulering i policyn.
