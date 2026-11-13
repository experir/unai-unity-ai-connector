# NPC dialogue

A small village square with two characters you can talk to. Click a villager, type what you want to say, and the NPC answers in character as the reply streams in.

- **Brom the blacksmith** has a persona and remembers what you told him for the whole session.
- **Mira the merchant** also runs a shop. She uses agent tools (`list_wares` and `sell_item`) to check her stock and actually sell you items. Your gold and inventory update in the corner of the screen.

## Run it

1. Install `com.unity.ugui` **2.0.0** (Unity UI and TextMeshPro) through Package Manager, keep UNAI's **Agent** module, then import this sample.
2. Create a config with **Window > UnAI > Setup Wizard** and choose a provider and model. Tool calling works best with models that support it natively (OpenAI, Anthropic, Gemini, and Ollama models with tool support).
3. Open `NpcDialogue.unity` and enter Play Mode. Import TextMeshPro essential resources if Unity asks.

In the Editor, the sample finds your `UnaiGlobalConfig` automatically. For a player build, assign it to the `UnaiManager` in the scene.

## How it works

- `UnaiNpc`: persona, greeting and memory. Each NPC owns a `UnaiAgent`, so conversations are separate and persist while the scene runs.
- `UnaiNpcShop`: implements `IUnaiNpcToolProvider` to give an NPC tools. The model decides when to sell; the C# code checks the price and the player's gold.
- `UnaiPlayerState`: gold and inventory that tools can change.
- `UnaiDialogueUI`: the dialogue box and HUD.

## Make your own NPC

Duplicate a villager, then edit **Display Name**, **Persona** and **Greeting** on its `UnaiNpc`. To give it abilities, add a component that implements `IUnaiNpcToolProvider`, for example opening a door, starting a quest or giving the player an item.

Shipping a game: keep shared cloud API keys on a backend you control, not in the build.
