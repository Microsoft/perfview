# PerfViewJS

PerfViewJS is a webviewer for ETL and NetPerf data.

## Development on VS Code

## Browser

This will run backend in debug mode

### Backend

- cmd+shift+D or launch backend via debug toolbar
- Place trace files in `"${workspaceFolder}/spa/tmp"`

### Frontend

- cd spa
- `npm run start`
- Browse to http://localhost:5000

## **or**

#### Electron

This will run backend in electron shell

- `npm run dev`

## Todo

- [ ] Sortable columns on numerical bug
- [ ] Make source viewer work
- [ ] Breadcrumbs on all pages
- [ ] Loading indicator & error handlers
- [ ] Understand what to do on "Lookup # of Symbols (min samples)"
- [ ] Understand what to do on "Process List" page, links of "Number of address occurrences in all stacks"
- [ ] Flame graph?
- [ ] Electron MAS notarization
- [ ] (is this still a todo?) Wrap PerfViewJS as a dotnet global tool
- [x] Electron splash screen
- [x] Refresh UI from HTML to Fluent-UI components
- [x] Use Chromium Embedded Framework to make a client-side application
- [x] Sortable Table (on tables which usually contains > 50 items)
