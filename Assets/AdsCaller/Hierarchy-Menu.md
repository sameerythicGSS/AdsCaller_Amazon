# Ads Caller Hierarchy menu

Right-click one scene GameObject and open **Ads Caller**. The same commands are available from **GameObject > Ads Caller**. Authoring actions require Edit Mode and one selected scene object. Missing prerequisites disable a command; no provider SDK is initialized by the editor menu.

| Option | Android Main | Amazon Main | CrossPromo only | Combined |
| --- | --- | --- | --- | --- |
| Add Panel Ad Behaviour | Yes | Yes | Yes | Yes |
| Bind Button: Interstitial, Rewarded, Show/Hide Banner, Show/Hide Large Banner, Hide All Banners | Yes | Yes | Yes | Yes |
| CrossPromo / Add PopAd as Child | Disabled | Disabled | Yes | Yes |
| Select Ads Manager | When a mediator exists | When a mediator exists | When a mediator exists | When a mediator exists |
| Open Assigned Configuration | When assigned | When assigned | When assigned | When assigned |
| Validate Selected Hierarchy | Yes | Yes | Yes | Yes |
| Setup Ads Caller in Scene | Existing wizard | Existing wizard | Existing wizard | Existing wizard |
| Play Mode / Log Diagnostics | With a live mediator | With a live mediator | With a live mediator | With a live mediator |

Android and Amazon are alternative Online providers. Combined means either Online provider plus CrossPromo. Unity 6 and Unity 2022 CrossPromo releases have the same menu. Core-only installations support shared authoring commands, but validation reports the missing provider; runtime ads require an installed and initialized provider. Shared banner and fullscreen commands use the mediator's configured routing, readiness, pacing, Remove Ads policy, and fallback behavior. The menu does not guarantee a network supports a format or that an ad is ready.

## PopAd

Install the matching CrossPromo module, then right-click a UI object inside a Canvas and choose **Ads Caller > CrossPromo > Add PopAd as Child**. The command adds the actual prefab directly below that object, preserves the prefab link, anchors, pivot and size, centers its anchored position, and resets local rotation/scale. It assigns the lowest unused nonnegative slot ID in the current scene, including inactive objects. It selects the result and supports Undo/Redo. Run validation after duplicating slots manually. IDs can still collide across separately authored scenes loaded additively; coordinate those IDs when designing such scenes.

PopAds require an initialized AdsCallerSalon at runtime. Adding a slot does not add managers or change provider configuration. The editor resolves assets by the existing GUID/index helper, supporting both Assets and UPM layouts.

## Panels and buttons

**Add Panel Ad Behaviour** adds or selects a shared component on the selected object. Configure Banner and Large Banner as None/Show/Hide, optional reversal on disable, and optional interstitial on enable. Delay uses real time so paused menu panels work. Pending work is cancelled on disable, and reversal occurs only for treatments that were applied. Reversal means applying the opposite action, rather than restoring an arbitrary previous banner state.

Button commands require a uGUI Button on the selected object. They add/reuse **Ads Button Action** and bind one persistent On Click listener. Rebinding replaces that component's chosen ad action, preserves unrelated listeners and reward callbacks, and avoids duplicate requests. Undo restores the previous action and binding.

For rewarded ads, select **Show Rewarded Ad…**, then wire **On Reward Earned** and **On Reward Failed** on the component in the Inspector. Grant currency/unlocks only from On Reward Earned; remove any older Button On Click listener that grants a reward directly. Validation reports an unwired reward event. Missing mediators fail rewarded requests through On Reward Failed without spawning a fallback manager.

## Validation and setup

Validation is read-only: it checks missing scripts, provider configurations, duplicate managers, PopAd references/Canvas/slot IDs, and unwired rewards. A missing manager in the inspected scene is reported as an initialization-scene requirement; it may intentionally live in another scene. Runtime diagnostics can find the persistent mediator after a scene transition. Setup opens the existing wizard; review its selected initialization scene and providers before applying. No command silently adds duplicate bootstrap objects or changes SDK credentials.
