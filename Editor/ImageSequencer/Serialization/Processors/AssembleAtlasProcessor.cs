using UnityEngine;

namespace UnityEditor.Experimental.VFX.Toolbox.ImageSequencer {
  [Processor("Texture Sheet", "Assemble Atlas")]
  internal class AssembleAtlasProcessor : ProcessorBase {
    public int FlipbookNumU => Size.x;

    public int FlipbookNumV => Size.y;

    Vector2Int Size {
      get {
        // Find the next power of 2 texture that contains every element in the input
        for (var i = 2; i < 64; i++) {
          int y = i / 2;
          int x = i - y;
          int xx = 1 << x;
          int yy = 1 << y;
          int sizeX = xx / inputSequenceWidth;
          int sizeY = yy / inputSequenceHeight;
          int totalSize = sizeX * sizeY;
          if (inputSequenceLength <= totalSize)
            return new Vector2Int(sizeX, sizeY);
        }

        return new Vector2Int(NextPowerOfTwo(inputSequenceLength * inputSequenceWidth) / inputSequenceWidth, 1);
      }
    }

    static int NextPowerOfTwo(int value) {
      var v = (uint)value;
      v--;
      v |= v >> 1;
      v |= v >> 2;
      v |= v >> 4;
      v |= v >> 8;
      v |= v >> 16;
      v++;
      return (int)v;
    }

    public override string shaderPath => "Packages/com.unity.vfx-toolbox/Editor/ImageSequencer/Shaders/AssembleBlit.shader";

    public override string processorName => "Assemble Atlas";

    public override string label
    {
	    get
	    {
		    try
		    {
			    return $"{processorName} ({FlipbookNumU}x{FlipbookNumV})";
		    }
		    catch
		    {
			    return base.label;
		    }
	    }
    }

    public override int numU => FlipbookNumU * inputSequenceNumU;

    public override int numV => FlipbookNumV * inputSequenceNumV;

    public override int sequenceLength => 1;

    public override void Default() {
    }

    public override void UpdateOutputSize() {
      SetOutputSize(inputSequenceWidth * FlipbookNumU, inputSequenceHeight * FlipbookNumV);
    }

    public override bool OnCanvasGUI(ImageSequencerCanvas canvas) {
      if (Event.current.type != EventType.Repaint)
        return false;

      Vector2 topRight = canvas.CanvasToScreen(
        new Vector2(-canvas.currentFrame.texture.width / 2, canvas.currentFrame.texture.height / 2)
      );

      Vector2 bottomLeft = canvas.CanvasToScreen(
        new Vector2(canvas.currentFrame.texture.width / 2, -canvas.currentFrame.texture.height / 2)
      );

      // Texts
      GUI.color = canvas.styles.green;
      for (var i = 0; i < FlipbookNumU; i++) {
        float cw = (topRight.x - bottomLeft.x) / FlipbookNumU;
        GUI.Label(new Rect(bottomLeft.x + i * cw, topRight.y - 16, cw, 16), (i + 1).ToString(), canvas.styles.miniLabelCenter);
      }

      for (var i = 0; i < FlipbookNumV; i++) {
        float ch = (bottomLeft.y - topRight.y) / FlipbookNumV;
        VFXToolboxGUIUtility.GUIRotatedLabel(new Rect(bottomLeft.x - 8, topRight.y + i * ch, 16, ch), (i + 1).ToString(), -90.0f, canvas.styles.miniLabelCenter);
      }

      GUI.color = Color.white;
      return false;
    }

    public override bool Process(int frame) {
      int length = inputSequenceLength;

      RenderTexture backup = RenderTexture.active;

      // Blit Every Image inside output
      for (var i = 0; i < inputSequenceLength; i++) {
        int u = i % FlipbookNumU;
        int v = (int)Mathf.Floor((float)i / FlipbookNumU);

        Vector2 size = new(1.0f / FlipbookNumU, 1.0f / FlipbookNumV);
        int idx = Mathf.Clamp(i, 0, length - 1);

        Texture currentTexture = RequestInputTexture(idx);

        Vector4 clipCoordinates = new(u * size.x, v * size.y, size.x, size.y);

        // ReSharper disable Unity.PreferAddressByIdToGraphicsParams
        material.SetTexture("_MainTex", currentTexture);
        material.SetVector("_CC", clipCoordinates);
        // ReSharper restore Unity.PreferAddressByIdToGraphicsParams

        Graphics.Blit(currentTexture, (RenderTexture)RequestOutputTexture(0), material);
      }

      RenderTexture.active = backup;

      return true;
    }

    public override bool OnInspectorGUI(bool changed, SerializedObject serializedObject) {
      EditorGUILayout.HelpBox("Size is the nearest power of 2 matching the input count.", MessageType.Info);
      return false;
    }
  }
}
