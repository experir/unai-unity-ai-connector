# Chat UI and agents

Before importing, install `com.unity.ugui` **2.0.0** through Package Manager; it provides Unity UI and TextMeshPro for Unity 6. Keep UNAI's Agent module installed. Import the sample through UNAI's Package Manager **Samples** section.

Create a `UnaiGlobalConfig` with **Window > UnAI > Setup Wizard**, configure your provider and model, open `UnaiExampleScene`, and assign the config to its `UnaiManager`. Import TextMeshPro essential resources if prompted. Enter Play Mode to use the chat demo.

`UnaiExampleAgent` demonstrates dice, time and calculator tools. It needs its serialized UI references assigned when added to a scene; it does not include a separate preconfigured agent scene.
