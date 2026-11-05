# Baskets and startup

## Sub-features

Initialize a usable first basket from empty storage; create and activate a second basket; discover a named folder copied directly into Baskets and open its contents; reject a second process while the first survives.

## How to get to it (user POV)

Launch PasteHatch. Choose + (Create basket) in the rail or Ctrl+T to add a basket. Launch again to show the existing instance.

## Driving it with UI Automation

Run scripts/verify.ps1. Readiness requires the actual UI and initialized storage. Later the helper invokes AddBasketFooterButton, waits for two persisted tabs, activeUid equal to the new tab and two backing folders, then invokes AddBasketButton and verifies a third active basket. baskets.ui.txt captures the rail; baskets.state.json captures active state. Settings checks toggle the basket deletion preference, close and reopen settings, and verify persistence before restoring confirmation. It launches a second owned process and checks that it exits while the primary remains healthy. Source: MainWindow.xaml.cs OnAddBasketClick, BasketWorkspace.CreateTab, App.xaml.cs OnLaunched, Platform/SingleInstanceHost.cs and Core/SingleInstanceGuard.cs.

## Gotchas

The helper copies repo-grooming-skill/SKILL.md directly into Baskets, waits for its persisted basket and rail item without a refresh command, selects it and checks the tree and exact preview content. external-basket.ui.txt and external-basket.state.json record this result.

Offscreen launches with an explicit data root use an isolated mutex and event; both test launches inherit the same root. The suite does not prove foreground activation, Ctrl+T, application restart recovery, arbitrary switching, rename, reorder, close confirmation or the 100-basket limit. Both launches inherit offscreen mode; foreground behavior is intentionally unverified.
