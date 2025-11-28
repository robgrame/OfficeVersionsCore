# Cookie Consent System - Testing Guide

## ?? Overview
Il sistema di consenso dei cookie è stato aggiornato con logging dettagliato per facilitare il debug e la verifica del funzionamento.

## ?? Come Testare

### 1. **Accedi alla Pagina di Test**
Avvia l'applicazione e visita: **`/CookieTest`**

Questa pagina mostra:
- Lo stato corrente del consenso
- Il GTM ID configurato
- Tutti i cookie attivi
- Se il banner è visibile
- Se GTM è stato caricato

### 2. **Apri Developer Tools**
- Premi **F12** o **Ctrl+Shift+I** (Windows/Linux) o **Cmd+Option+I** (Mac)
- Vai alla tab **Console**
- Opzionalmente, filtra per "Cookie Consent" per vedere solo i log rilevanti

### 3. **Verifica i Log della Console**

Quando ricarichi la pagina dovresti vedere questi messaggi:

```
[Cookie Consent] Script loaded - waiting for DOM ready
[Cookie Consent] DOM ready - creating CookieConsent instance
[Cookie Consent] Initializing Cookie Consent Manager
[Cookie Consent] Reading all cookies: ...
```

#### **Scenario 1: Nessun Consenso (prima visita)**
```
[Cookie Consent] Current consent status: null
[Cookie Consent] No consent found - showing banner
[Cookie Consent] Creating and displaying banner
[Cookie Consent] Banner inserted into DOM
```

#### **Scenario 2: Consenso Accettato**
```
[Cookie Consent] Current consent status: true
[Cookie Consent] Consent granted - loading GTM
[Cookie Consent] Loading Google Tag Manager
[Cookie Consent] GTM ID: GTM-TBRB6FD
[Cookie Consent] Injecting GTM script for ID: GTM-TBRB6FD
[Cookie Consent] GTM script injected successfully
[Cookie Consent] GTM noscript fallback added
```

#### **Scenario 3: Consenso Rifiutato**
```
[Cookie Consent] Current consent status: false
[Cookie Consent] Consent denied - GTM not loaded
```

### 4. **Verifica i Cookie**
- Vai alla tab **Application** in Developer Tools
- Espandi **Cookies** nel menu laterale
- Seleziona il tuo dominio (es. `http://localhost:5000`)
- Cerca il cookie: **`office365versions_cookie_consent`**
  - **Valore `true`**: consenso accettato
  - **Valore `false`**: consenso rifiutato
  - **Cookie assente**: nessun consenso ancora dato

### 5. **Test Interattivi nella Pagina `/CookieTest`**

La pagina offre pulsanti per testare vari scenari:

- **Simulate Accept**: Imposta il consenso come accettato
- **Simulate Reject**: Imposta il consenso come rifiutato
- **Clear Consent**: Cancella il cookie di consenso (ricarica per vedere il banner)
- **Refresh Status**: Aggiorna lo stato visualizzato nella tabella

## ?? Verifica GTM (Google Tag Manager)

### Metodo 1: Console
Dopo aver accettato i cookie, verifica che GTM sia stato caricato:
```javascript
// In console, digita:
window.dataLayer
// Dovresti vedere un array con oggetti GTM
```

### Metodo 2: Network Tab
- Vai alla tab **Network** in Developer Tools
- Ricarica la pagina dopo aver accettato i cookie
- Cerca richieste verso `googletagmanager.com`
- Dovresti vedere richieste a `gtm.js?id=GTM-TBRB6FD`

### Metodo 3: Google Tag Assistant (Chrome Extension)
- Installa [Tag Assistant](https://chrome.google.com/webstore/detail/tag-assistant-legacy-by-g/kejbdjndbnbjgmefkgdddjlbokphdefk)
- Clicca sull'icona dopo aver accettato i cookie
- Dovresti vedere GTM come "Working"

## ?? Troubleshooting

### Il Banner non Appare
1. Verifica nella Console se vedi: `[Cookie Consent] Script loaded`
2. Controlla che il file `cookie-consent.js` sia caricato correttamente (tab Network)
3. Verifica che non ci siano errori JavaScript nella Console

### GTM non si Carica
1. Verifica che `window.gtmId` sia impostato (digita in console)
2. Assicurati di aver **accettato** i cookie (non rifiutato)
3. Controlla i log: `[Cookie Consent] GTM ID: ...`
4. Verifica che il GTM ID in `appsettings.json` sia corretto

### I Log non Appaiono
1. Svuota la cache del browser (**Ctrl+Shift+Del**)
2. Ricarica con cache svuotata (**Ctrl+F5**)
3. Verifica che la Console non abbia filtri attivi

## ?? Configurazione

### File di Configurazione
Il GTM ID è configurato in `appsettings.json`:
```json
"Google": {
  "Tag": "GTM-TBRB6FD"
}
```

### File JavaScript
Il sistema di consenso è implementato in:
- **`wwwroot/js/cookie-consent.js`**: Logica principale
- **`wwwroot/css/cookie-consent.css`**: Stili del banner

### Layout
Il GTM ID viene iniettato nel layout (`Pages/Shared/_Layout.cshtml`):
```javascript
<script>
    window.gtmId = '@Configuration["Google:Tag"]';
</script>
```

## ? Checklist di Verifica

- [ ] Il banner appare alla prima visita
- [ ] Il banner scompare dopo aver cliccato "Accept" o "Decline"
- [ ] Il cookie `office365versions_cookie_consent` viene salvato correttamente
- [ ] GTM si carica **solo** dopo aver accettato i cookie
- [ ] GTM **non** si carica se i cookie vengono rifiutati
- [ ] Il banner riappare dopo aver cancellato il cookie
- [ ] I log nella Console mostrano tutti i passaggi
- [ ] La pagina `/CookieTest` mostra lo stato corretto

## ?? Note Aggiuntive

### Privacy-First Design
- **Consenso esplicito**: GTM si carica SOLO dopo il consenso
- **Rispetto della scelta**: Se l'utente rifiuta, GTM non viene mai caricato
- **Persistenza**: Il consenso viene salvato per 1 anno

### GDPR Compliance
? Il sistema rispetta i requisiti GDPR:
- Consenso esplicito richiesto
- Informazioni chiare sul tipo di dati raccolti
- Possibilità di rifiutare
- Link alla Privacy Policy

## ?? Deploy su Azure

Quando effettui il deploy su Azure, assicurati che:
1. Il GTM ID in `appsettings.json` sia corretto per produzione
2. La sezione `Google:Tag` sia presente nelle Application Settings di Azure
3. I file `cookie-consent.js` e `cookie-consent.css` siano pubblicati correttamente

Per verificare su Azure:
1. Visita `https://tuosito.azurewebsites.net/CookieTest`
2. Apri Developer Tools
3. Segui gli stessi passi di test descritti sopra

---

**Creato da**: Office 365 Versions Team  
**Ultima modifica**: 2025-01-28
