// Builds the sample scenes for InGameChat, NpcDialogue and EditorAssistantPlayground.
// Runs inside a temporary project prepared by build_sample_scenes.py; not shipped with the package.
using System.IO;
using TMPro;
using UnAI.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Chat = UnAI.Samples.InGameChat;
using Npc = UnAI.Samples.NpcDialogue;
using Play = UnAI.Samples.EditorAssistantPlayground;

public static class UnaiSampleSceneBuilder
{
    private static readonly Color Green = new(0f, 0.62f, 0f);           // UNAI green, readable with white text
    private static readonly Color PanelDark = new(0.11f, 0.12f, 0.15f, 0.97f);
    private static readonly Color PanelMid = new(0.2f, 0.22f, 0.27f, 1f);
    private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
    private static readonly Color TextDim = new(0.62f, 0.65f, 0.7f);

    public static void BuildAll()
    {
        if (TMP_Settings.defaultFontAsset == null)
            throw new System.Exception("TMP Essential Resources are missing; import them before building the scenes.");
        BuildInGameChat();
        BuildNpcDialogue();
        BuildPlayground();
        AssetDatabase.SaveAssets();
        Debug.Log("[UNAI samples] All sample scenes built.");
    }

    // ---------------------------------------------------------------- In-game chat

    private static void BuildInGameChat()
    {
        const string folder = "Assets/InGameChat";
        var rounded = RoundedSprite(folder, 18);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f);

        new GameObject("UNAI Manager").AddComponent<UnaiManager>();
        new GameObject("EventSystem").AddComponent<EventSystem>();
        var canvas = CreateCanvas("Canvas");

        // Placeholder "game" behind the chat.
        var game = Text(canvas.transform, "Your Game Here", "Your game here", 64, new Color(1, 1, 1, 0.08f), FontStyles.Bold, TextAlignmentOptions.Center);
        Place(game.rectTransform, new Vector2(0, 0), new Vector2(0.62f, 1), Vector2.zero, Vector2.zero);

        var window = Panel(canvas.transform, "Chat Window", PanelDark, rounded);
        Place(window.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-40, 40), new Vector2(-40, -40), new Vector2(1, 0.5f));
        window.rectTransform.offsetMin = new Vector2(-40 - 600, 40);

        // Header
        var header = Empty(window.transform, "Header");
        Place(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -84), new Vector2(0, 0));
        var title = Text(header, "Title", "Nova", 30, TextMain, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(24, 0), new Vector2(-170, -12));
        title.alignment = TextAlignmentOptions.BottomLeft;
        var provider = Text(header, "Provider", "Provider · model", 18, TextDim);
        Place(provider.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(24, 10), new Vector2(-170, 0));
        provider.alignment = TextAlignmentOptions.TopLeft;
        var newChat = MakeButton(header, "New Chat Button", "New chat", PanelMid, TextMain, rounded, 20, out _);
        Place((RectTransform)newChat.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-152, -24), new Vector2(-20, 24));
        var divider = Panel(window.transform, "Divider", new Color(1, 1, 1, 0.07f), null);
        Place(divider.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -86), new Vector2(0, -84));

        // Messages
        var scroll = ScrollArea(window.transform, "Messages", out var content);
        Place((RectTransform)scroll.transform, Vector2.zero, Vector2.one, new Vector2(0, 92), new Vector2(0, -86));
        var player = BubbleTemplate(content, "Player Bubble Template", Green, Color.white, rounded, TextAnchor.UpperRight);
        var assistant = BubbleTemplate(content, "Assistant Bubble Template", PanelMid, TextMain, rounded, TextAnchor.UpperLeft);
        var system = BubbleTemplate(content, "System Bubble Template", new Color(0.55f, 0.15f, 0.15f, 0.35f), new Color(1f, 0.78f, 0.78f), rounded, TextAnchor.UpperCenter);

        // Footer
        var footer = Empty(window.transform, "Footer");
        Place(footer, new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 16), new Vector2(-16, 76));
        var input = MakeInput(footer, "Message Input", "Type a message…", rounded);
        Place((RectTransform)input.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-124, 0));
        var send = MakeButton(footer, "Send Button", "Send", Green, Color.white, rounded, 22, out var sendLabel);
        Place((RectTransform)send.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-112, 0), Vector2.zero);

        var chat = window.gameObject.AddComponent<Chat.UnaiChatWindow>();
        var so = new SerializedObject(chat);
        Set(so, "_scroll", scroll);
        Set(so, "_messageList", content);
        Set(so, "_playerBubbleTemplate", player);
        Set(so, "_assistantBubbleTemplate", assistant);
        Set(so, "_systemBubbleTemplate", system);
        Set(so, "_input", input);
        Set(so, "_sendButton", send);
        Set(so, "_sendButtonLabel", sendLabel);
        Set(so, "_newChatButton", newChat);
        Set(so, "_providerLabel", provider);
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(scene, $"{folder}/InGameChat.unity");
    }

    private static GameObject BubbleTemplate(RectTransform content, string name, Color bg, Color fg, Sprite sprite, TextAnchor align)
    {
        var row = Empty(content, name);
        var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = align;
        rowLayout.childControlWidth = rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

        var bubble = Panel(row, "Bubble", bg, sprite);
        var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
        bubbleLayout.padding = new RectOffset(18, 18, 12, 12);
        bubbleLayout.childControlWidth = bubbleLayout.childControlHeight = true;
        bubbleLayout.childForceExpandWidth = bubbleLayout.childForceExpandHeight = false;
        bubble.gameObject.AddComponent<LayoutElement>().preferredWidth = 200;

        var label = Text(bubble.transform, "Text", "Message", 22, fg);
        label.textWrappingMode = TextWrappingModes.Normal;
        if (align == TextAnchor.UpperCenter) label.fontStyle = FontStyles.Italic;
        return row.gameObject;
    }

    // ---------------------------------------------------------------- NPC dialogue

    private static void BuildNpcDialogue()
    {
        const string folder = "Assets/NpcDialogue";
        var rounded = RoundedSprite(folder, 16);
        var circle = CircleSprite(folder);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(0, 5.2f, -8.5f), Quaternion.Euler(24, 0, 0));
        cam.fieldOfView = 50;
        cam.gameObject.AddComponent<PhysicsRaycaster>();
        var sun = Object.FindAnyObjectByType<Light>();
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);

        new GameObject("UNAI Manager").AddComponent<UnaiManager>();
        new GameObject("Player State").AddComponent<Npc.UnaiPlayerState>();
        new GameObject("EventSystem").AddComponent<EventSystem>();

        var world = new GameObject("Village Square").transform;
        Prop(world, "Ground", PrimitiveType.Plane, new Vector3(0, 0, 8), new Vector3(6, 1, 4), new Color(0.47f, 0.6f, 0.38f));
        Prop(world, "Path", PrimitiveType.Cube, new Vector3(0, 0.01f, 2), new Vector3(2.2f, 0.02f, 20), new Color(0.72f, 0.64f, 0.5f));
        // Smithy
        Prop(world, "Anvil", PrimitiveType.Cube, new Vector3(-3.6f, 0.35f, 0.8f), new Vector3(1f, 0.7f, 0.5f), new Color(0.25f, 0.26f, 0.28f));
        Prop(world, "Forge", PrimitiveType.Cube, new Vector3(-4.6f, 0.9f, 2.6f), new Vector3(2f, 1.8f, 1.4f), new Color(0.42f, 0.36f, 0.34f));
        Prop(world, "Embers", PrimitiveType.Cube, new Vector3(-4.6f, 1.05f, 1.88f), new Vector3(1.2f, 0.5f, 0.05f), new Color(1f, 0.45f, 0.1f));
        // Shop
        Prop(world, "Counter", PrimitiveType.Cube, new Vector3(3.2f, 0.5f, 1.2f), new Vector3(2.6f, 1f, 0.7f), new Color(0.55f, 0.37f, 0.22f));
        Prop(world, "Awning", PrimitiveType.Cube, new Vector3(3.2f, 3.1f, 1.6f), new Vector3(3.2f, 0.12f, 1.6f), new Color(0.85f, 0.3f, 0.3f));
        Prop(world, "Awning Post L", PrimitiveType.Cylinder, new Vector3(1.75f, 1.5f, 2.3f), new Vector3(0.1f, 1.5f, 0.1f), new Color(0.45f, 0.3f, 0.18f));
        Prop(world, "Awning Post R", PrimitiveType.Cylinder, new Vector3(4.65f, 1.5f, 2.3f), new Vector3(0.1f, 1.5f, 0.1f), new Color(0.45f, 0.3f, 0.18f));
        Prop(world, "Crate A", PrimitiveType.Cube, new Vector3(5f, 0.35f, 0.6f), new Vector3(0.7f, 0.7f, 0.7f), new Color(0.65f, 0.48f, 0.3f));
        Prop(world, "Crate B", PrimitiveType.Cube, new Vector3(5.1f, 0.95f, 0.65f), new Vector3(0.5f, 0.5f, 0.5f), new Color(0.6f, 0.44f, 0.27f));
        Prop(world, "Potion", PrimitiveType.Sphere, new Vector3(2.6f, 1.15f, 1.1f), new Vector3(0.28f, 0.28f, 0.28f), new Color(0.85f, 0.15f, 0.25f));
        Prop(world, "Lantern", PrimitiveType.Cylinder, new Vector3(3.6f, 1.18f, 1.1f), new Vector3(0.22f, 0.18f, 0.22f), new Color(0.95f, 0.8f, 0.3f));

        var brom = Villager("Brom", "Blacksmith", new Vector3(-2.3f, 1, -0.2f), new Color(0.82f, 0.45f, 0.22f),
            "Brom is the village blacksmith: gruff, proud of his work, secretly kind. Two days ago wolves attacked " +
            "his iron shipment near the north bridge, and he cannot forge new blades until someone brings the iron back. " +
            "He knows Mira the merchant sells his Iron Swords. He speaks plainly and calls the player 'traveller'.",
            "Hm. A new face. If you're after a new blade, you're out of luck: no iron since the wolves hit my cart at the north bridge.");
        var mira = Villager("Mira", "Merchant", new Vector3(2.3f, 1, 0.2f), new Color(0.27f, 0.55f, 0.9f),
            "Mira runs the general store on the village square: cheerful, quick-witted and a little nosy. She loves " +
            "village gossip and always tries to make a sale. She knows Brom the blacksmith is grumpy because of the stolen iron.",
            "Welcome, welcome! Potions, lanterns, maps, even one of Brom's swords. What can I get you?");
        mira.gameObject.AddComponent<Npc.UnaiNpcShop>();
        brom.transform.SetParent(world);
        mira.transform.SetParent(world);

        // UI
        var canvas = CreateCanvas("Canvas");
        var hintBar = Panel(canvas.transform, "Hint Bar", new Color(0.08f, 0.09f, 0.11f, 0.85f), rounded);
        Place(hintBar.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-420, -80), new Vector2(420, -24));
        var hint = Text(hintBar.transform, "Hint", "Click a villager to talk", 22, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0));

        var hud = Panel(canvas.transform, "HUD", new Color(0.08f, 0.09f, 0.11f, 0.85f), rounded);
        Place(hud.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -120), new Vector2(460, -24));
        var gold = Text(hud.transform, "Gold", "50 gold", 28, new Color(1f, 0.83f, 0.3f), FontStyles.Bold);
        Place(gold.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-20, -14));
        var inventory = Text(hud.transform, "Inventory", "Inventory: empty", 20, TextMain);
        Place(inventory.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 14), new Vector2(-20, -56));

        var panel = Panel(canvas.transform, "Dialogue Panel", new Color(0.08f, 0.09f, 0.11f, 0.94f), rounded);
        Place(panel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-560, 32), new Vector2(560, 322));
        var portrait = Panel(panel.transform, "Portrait", Color.white, circle);
        portrait.type = Image.Type.Simple;
        Place(portrait.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -112), new Vector2(112, -28));
        var npcName = Text(panel.transform, "Name", "Name", 30, Color.white, FontStyles.Bold);
        Place(npcName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(136, -66), new Vector2(-180, -26));
        var action = Text(panel.transform, "Action", "", 20, TextDim, FontStyles.Italic);
        Place(action.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(136, -96), new Vector2(-180, -66));
        var line = Text(panel.transform, "Line", "…", 26, TextMain);
        line.textWrappingMode = TextWrappingModes.Normal;
        Place(line.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(136, 104), new Vector2(-32, -100));
        var leave = MakeButton(panel.transform, "Leave Button", "Leave", PanelMid, TextMain, rounded, 20, out _);
        Place((RectTransform)leave.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-148, -72), new Vector2(-28, -28));
        var input = MakeInput(panel.transform, "Reply Input", "Say something…", rounded);
        Place((RectTransform)input.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(136, 28), new Vector2(-164, 84));
        var send = MakeButton(panel.transform, "Send Button", "Say", Green, Color.white, rounded, 22, out _);
        Place((RectTransform)send.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-148, 28), new Vector2(-28, 84));

        var ui = canvas.gameObject.AddComponent<Npc.UnaiDialogueUI>();
        var so = new SerializedObject(ui);
        Set(so, "_panel", panel.gameObject);
        Set(so, "_portrait", portrait);
        Set(so, "_nameLabel", npcName);
        Set(so, "_lineLabel", line);
        Set(so, "_actionLabel", action);
        Set(so, "_input", input);
        Set(so, "_sendButton", send);
        Set(so, "_leaveButton", leave);
        Set(so, "_hintLabel", hint);
        Set(so, "_goldLabel", gold);
        Set(so, "_inventoryLabel", inventory);
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(scene, $"{folder}/NpcDialogue.unity");
    }

    private static Npc.UnaiNpc Villager(string name, string role, Vector3 position, Color color, string persona, string greeting)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = name;
        body.transform.position = position;
        body.AddComponent<Npc.UnaiSampleTint>().Color = color;
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.transform.SetParent(body.transform, false);
        head.transform.localPosition = new Vector3(0, 0.95f, 0);
        head.transform.localScale = Vector3.one * 0.62f;
        head.AddComponent<Npc.UnaiSampleTint>().Color = new Color(0.96f, 0.8f, 0.66f);

        var label = new GameObject("Name Label").AddComponent<TextMeshPro>();
        label.transform.SetParent(body.transform, false);
        label.transform.localPosition = new Vector3(0, 2.25f, 0);
        label.transform.rotation = Quaternion.Euler(24, 0, 0);
        label.rectTransform.sizeDelta = new Vector2(4, 1.2f);
        label.text = $"<b>{name}</b>\n<size=65%>{role}</size>";
        label.fontSize = 4.2f;
        label.alignment = TextAlignmentOptions.Bottom;
        label.color = Color.white;

        var npc = body.AddComponent<Npc.UnaiNpc>();
        var so = new SerializedObject(npc);
        so.FindProperty("_displayName").stringValue = name;
        so.FindProperty("_color").colorValue = color;
        so.FindProperty("_persona").stringValue = persona;
        so.FindProperty("_greeting").stringValue = greeting;
        so.ApplyModifiedPropertiesWithoutUndo();
        return npc;
    }

    private static void Prop(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.AddComponent<Npc.UnaiSampleTint>().Color = color;
    }

    // ---------------------------------------------------------------- Editor Assistant playground

    private static void BuildPlayground()
    {
        const string folder = "Assets/EditorAssistantPlayground";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(0, 6, -11), Quaternion.Euler(12, 0, 0));

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2, 1, 2);
        ground.AddComponent<Play.UnaiSampleTint>().Color = new Color(0.72f, 0.74f, 0.76f);

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Instructions Board";
        board.transform.position = new Vector3(0, 4.3f, 7);
        board.transform.localScale = new Vector3(17, 8.2f, 0.2f);
        board.AddComponent<Play.UnaiSampleTint>().Color = new Color(0.1f, 0.11f, 0.13f);

        var text = new GameObject("Instructions Text").AddComponent<TextMeshPro>();
        text.transform.position = new Vector3(0, 4.3f, 6.88f);
        text.rectTransform.sizeDelta = new Vector2(15.6f, 7.4f);
        text.fontSize = 4.2f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        const string n = "<color=#00BD00><b>";
        text.text =
            "<size=180%><b>Editor Assistant Playground</b></size>\n" +
            "<color=#9aa0a6>This scene is intentionally empty. Build it by chatting. No Play Mode needed.</color>\n\n" +
            n + "1</b></color><indent=4%>Open <b>Window > UnAI > AI Assistant</b></indent>\n" +
            n + "2</b></color><indent=4%>Ask it to build something, for example:</indent>\n" +
            "<indent=7%><i>\"Create a 5x5 grid of cubes with random colors on the ground.\"</i></indent>\n" +
            "<indent=7%><i>\"Add a Rigidbody to every cube, drop them from 5 meters, and press Play.\"</i></indent>\n" +
            "<indent=7%><i>\"Create a warm orange point light above the center.\"</i></indent>\n" +
            "<indent=7%><i>\"Write a script that slowly rotates the selected object.\"</i></indent>\n" +
            n + "3</b></color><indent=4%>Scene changes go through Undo (Ctrl+Z).</indent>\n\n" +
            "<color=#9aa0a6>Using another AI client? <b>Window > UnAI > MCP Server</b> exposes the same tools.\n" +
            "Select <b>READ ME</b> in the Hierarchy for details.</color>";

        new GameObject("READ ME").AddComponent<Play.UnaiPlaygroundNotes>();
        Save(scene, $"{folder}/EditorAssistantPlayground.unity");
    }

    // ---------------------------------------------------------------- UI helpers

    private static Canvas CreateCanvas(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static RectTransform Empty(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Panel(Transform parent, string name, Color color, Sprite sprite)
    {
        var image = Empty(parent, name).gameObject.AddComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, string value, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Empty(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = align;
        text.raycastTarget = false;
        return text;
    }

    private static Button MakeButton(Transform parent, string name, string label, Color bg, Color fg, Sprite sprite, float size, out TMP_Text labelText)
    {
        var image = Panel(parent, name, bg, sprite);
        var button = image.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        button.colors = colors;
        var text = Text(image.transform, "Label", label, size, fg, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        labelText = text;
        return button;
    }

    private static TMP_InputField MakeInput(Transform parent, string name, string placeholder, Sprite sprite)
    {
        var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources { inputField = sprite, background = sprite, standard = sprite });
        go.name = name;
        go.transform.SetParent(parent, false);
        var field = go.GetComponent<TMP_InputField>();
        var image = go.GetComponent<Image>();
        image.color = new Color(0.17f, 0.19f, 0.23f);
        image.type = Image.Type.Sliced;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.textComponent.fontSize = 22;
        field.textComponent.color = TextMain;
        field.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        var ph = (TMP_Text)field.placeholder;
        ph.text = placeholder;
        ph.fontSize = 22;
        ph.color = TextDim;
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        field.caretColor = TextMain;
        field.customCaretColor = true;
        field.selectionColor = new Color(0f, 0.74f, 0f, 0.35f);
        var area = (RectTransform)field.textViewport;
        area.offsetMin = new Vector2(16, 6);
        area.offsetMax = new Vector2(-16, -6);
        return field;
    }

    private static ScrollRect ScrollArea(Transform parent, string name, out RectTransform content)
    {
        var root = Empty(parent, name);
        root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // receives drag/scroll
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;

        var viewport = Empty(root, "Viewport");
        Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = Empty(viewport, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 10;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Vector2? pivot = null)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        if (pivot.HasValue) rt.pivot = pivot.Value;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void Set(SerializedObject so, string field, Object value)
    {
        var property = so.FindProperty(field);
        if (property == null) throw new System.Exception($"Field '{field}' not found on {so.targetObject.GetType().Name}");
        property.objectReferenceValue = value;
    }

    private static void Save(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (!EditorSceneManager.SaveScene(scene, path)) throw new System.Exception($"Could not save {path}");
        Debug.Log($"[UNAI samples] Saved {path}");
    }

    // ---------------------------------------------------------------- Sprites

    private static Sprite RoundedSprite(string folder, int radius) =>
        MakeSprite($"{folder}/Art/RoundedRect.png", 64, radius, radius);

    private static Sprite CircleSprite(string folder) =>
        MakeSprite($"{folder}/Art/Circle.png", 128, 64, 0);

    private static Sprite MakeSprite(string path, int size, int radius, int border)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
            tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f)));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
