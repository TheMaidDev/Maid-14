# Bush disguise

An empty-handed player can pick up a potted plant (`PottedPlant*`) and become that plant: the player's own sprite is hidden, the plant is drawn in their place, and name, examine text, chat and identity are the plant's. Dropping the plant for any reason ends it. Code lives in the `_Maid` folders of Shared, Client and Server (`BushDisguise`).

## Pieces
- `BushDisguiseComponent` is added by the abstract `PottedPlantBushBase` (between `PottedPlantBase` and every `PottedPlant*`), together with `Item` (huge) and `MultiHandedItem`. Prototypes cannot remove inherited components, which is why `DimensionPot` stays directly on `PottedPlantBase`: it keeps the mob-like physics but is not an item and cannot disguise anyone. The component holds the pickup delay, the visual prototype and the maximum drop distance; the delay and distance are the tunables.
- Being a `MultiHandedItem`, carrying a plant fills two hands. The system additionally fills any further free hands with virtual items, so mobs with more hands are fully blocked. Virtual items go away through `MultiHandedItem` when the plant leaves the hand; the plant's equip handler runs after `MultiHandedItem`'s so a rejected plant removes them again.
- `BushDisguisedComponent` is added to the holder (its networked fields are the plant and the visual entity). `MaidBushDisguiseVisual` is a networked entity parented to the holder, with `BushDisguiseVisualComponent` naming its source plant and prototype, and a `TargetInteractionRelayComponent` so hits, pulls and clicks on it land on the real player.

## Flow
- Ordinary pickup is blocked: `GettingPickedUpAttemptEvent` is cancelled unless `PickupAllowed` is set. The do-after completion sets it only while it calls one `TryPickup`. Click and the pickup verb both go to the do-after instead (the vanilla pickup verbs are removed from the verb list). Pickup requires every hand to be empty; a plant that reaches a hand some other way (stripping puts it there with the blockers off) is dropped again unless the other hands are empty too. When a `SecretStash` is open and holds an item, its handler hands the item out first; otherwise the plant's handler starts the do-after.
- Disguise and undisguise are server-only and driven by the plant's hand equip/unequip events and the plant's component shutdown. Mob state leaving Alive forces a drop. Identity is refreshed through `GetIdentityRepresentationEntityEvent`; the speaker name, speech verb, speech/emote sounds and short identity info are taken from the plant (radio and telephone transmissions, flagged `IsRadio` on `TransformSpeakerNameEvent` by the radio and telephone systems, keep the real name); attacks are cancelled; Maid TTS skips holders.
- Dropping respects a clamp in the shared hands drop code (marked `MAID`) that limits how far from the user a plant lands; throwing is unaffected.
- The hand slot of someone pulling a disguised player shows the visual instead of the player (`GetVirtualItemBlockingEntityEvent`, only while being pulled so carrying keeps working). The puller's virtual item is retargeted when the disguise starts or ends mid-pull, otherwise the pull logic would compare against a deleted entity.

## Client
- The visual starts invisible. It copies the first sprite layer, RSI state and draw depth of the live plant, falling back to the plant prototype's sprite until the live one is known (then upgrades). The visual's own offset comes from its prototype.
- The holder's sprite is hidden only after the visual is ready, in the same frame. A per-frame recheck un-hides a holder whose visual vanished and re-hides one that something else made visible. Nothing is predicted: everything waits for server state, so the swap costs one network round trip.

## Plant physics
- Plants are mob-like: dynamic body, a hard fixture with no layer (only walls block it, nothing is blocked by it, bullets and sight pass), a non-hard `flammable` fixture carrying the mob layer, and `MobCollision`. Mobs and plants soft-push each other through the mob collision system, which only runs when the `movement.mob_pushing` CVar is on (default off; the development and WizardsDen presets turn it on).
- `SpriteFade` was removed from `PottedPlantBase`.

## Upstream touch points
- `ChameleonProjector` (client) copies the source sprite with the engine's `SpriteSystem.CopySprite`, which keeps layer map keys; serializer copy loses them.

## Known gaps
- `OfferItem` cannot hand over a plant: its receive step uses the ordinary pickup, which the cancel blocks.
- `DimensionPot` inherits all of this.
