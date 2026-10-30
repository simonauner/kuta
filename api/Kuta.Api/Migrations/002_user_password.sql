-- Lösenordsinloggning. Hashen är ASP.NET Core Identitys PasswordHasher-format (PBKDF2).
-- users är tom när detta körs, därav not null utan default.
alter table users add column password_hash text not null;
