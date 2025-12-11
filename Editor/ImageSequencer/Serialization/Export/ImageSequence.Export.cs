#if UNITY_2022_2_OR_NEWER && SPRITE_PACKAGE
#define USES_SPRITE_DATA_PROVIDER
using UnityEditor.U2D.Sprites;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VFXToolbox.MiniTGA;
using Object = UnityEngine.Object;

namespace UnityEditor.Experimental.VFX.Toolbox.ImageSequencer
{
	public static class ImageSequenceExport
	{
		public static string CreateSpriteSheet(List<Sprite> sprites, string outputFilePathWithoutExtension, ushort spriteSizeX, ushort spriteSizeY)
		{
			var sequence = ScriptableObject.CreateInstance<ImageSequence>();

			// Construct sprite sheet settings.
			sequence.exportSettings = new ImageSequence.ExportSettings
			{
				fileName = outputFilePathWithoutExtension.Replace('\\', '/') + ".png",
				frameCount = 1,
				outputShape = ImageSequence.OutputMode.Texture2D,
				exportMode = ImageSequence.ExportMode.PNG,
				exportAlpha = true,
				exportSeparateAlpha = false,
				sRGB = true,
				highDynamicRange = false,
				compress = true,
				generateMipMaps = false,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				dataContents = ImageSequence.DataContents.Sprite,
				spriteNameFormat = ImageSequence.SpriteNameFormat.InputNames
			};
			var resizeProcessor = ScriptableObject.CreateInstance<ResizeProcessor>();
			resizeProcessor.Width = spriteSizeX;
			resizeProcessor.Height = spriteSizeY;
			AddProcessor(resizeProcessor);
			AddProcessor(ScriptableObject.CreateInstance<AssembleAtlasProcessor>());
			sequence.inputFrameAssets = sprites.OfType<Object>().ToList();

			// Export & cleanup.
			string result = sequence.ExportToFile(true);
			foreach (ProcessorInfo processorInfo in sequence.processorInfos)
			{
				Object.DestroyImmediate(processorInfo.Settings);
				Object.DestroyImmediate(processorInfo);
			}
			
			Object.DestroyImmediate(sequence);
			return result;

			void AddProcessor(ProcessorBase processor)
			{
				var processorInfo = ScriptableObject.CreateInstance<ProcessorInfo>();
				processorInfo.Enabled = true;
				processorInfo.Settings = processor;
				sequence.processorInfos.Add(processorInfo);
			}
		}
	}
	
	internal partial class ImageSequence
	{
		private string GetNumberedFileName(string pattern, int number, int maxFrames)
        {
            int numbering = (int)Mathf.Floor(Mathf.Log10(maxFrames))+1;
            return pattern.Replace("#", number.ToString("D" + numbering.ToString()));
        }

		public string ExportToFile(bool useCurrentFileName)
		{
			var stack = new ProcessingNodeStack(new ProcessingFrameSequence(null), null);
			stack.LoadFramesFromAsset(this);
			stack.LoadProcessorsFromAsset(this);
			stack.InvalidateAll();
			return ExportToFile(
				stack,
				useCurrentFileName
			);
		}

		public string ExportToFile(ProcessingNodeStack processingNodeStack, bool useCurrentFileName)
        {
            bool bIsInsideProject = true;
            string path;
            if(useCurrentFileName)
            {
                path = exportSettings.fileName;
            }
            else
            {
                string title = "Save Texture, use # for frame numbering.";
                string defaultFileName, extension;

                int count = processingNodeStack.outputSequence.frames.Count;
                int numU = processingNodeStack.outputSequence.numU;
                int numV = processingNodeStack.outputSequence.numV;

                string defaultDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(this));

                defaultFileName =  name;

                if (count > 1)
                    defaultFileName += "_#";

                if(numU * numV != 1)
                    defaultFileName += "_"+numU+"x"+numV;

                switch (exportSettings.exportMode)
                {
                    case ExportMode.EXR:
                        defaultFileName += ".exr";
                        extension = "exr";

                        break;
                    case ExportMode.Targa:
                        defaultFileName += ".tga";
                        extension = "tga";

                        break;
                    case ExportMode.PNG:
                        defaultFileName += ".png";
                        extension = "png";

                        break;
                    default: return null;
                }

                 path = EditorUtility.SaveFilePanel(title, defaultDir, defaultFileName, extension);
                if (path == null || path == "")
                    return "";

                if (path.Contains(Application.dataPath))
                    path = path.Replace(Application.dataPath, "Assets");

            }

            if(!path.StartsWith("Assets/"))
            {
                bIsInsideProject = false;
                Debug.LogWarning("VFX Toolbox Warning : Saving a texture outside the project's scope. Import Settings will not be applied");
            }

            int frameCount = processingNodeStack.outputSequence.length;

            if(frameCount > 1 && !Path.GetFileNameWithoutExtension(path).Contains("#"))
            {
                if (!EditorUtility.DisplayDialog("VFX Toolbox", "You are currently exporting a sequence of images with no # in filename for numbering, do you want to add _# as a suffix of the filename?", "Add Postfix", "Cancel Export"))
                    return "";

                string newpath = Path.GetDirectoryName(path) + "\\" + Path.GetFileNameWithoutExtension(path) + "_#" + Path.GetExtension(path);
                path = newpath;
            }

            ExportSettings settings = exportSettings;
            bool bCanceled = false;

            try
            {
                int i = 1;
                foreach (ProcessingFrame frame in processingNodeStack.outputSequence.frames)
                {
                    if (VFXToolboxGUIUtility.DisplayProgressBar("Image Sequencer", "Exporting Frame #" + i + "/" + frameCount, (float)i / frameCount, 0, true))
                    {
                        bCanceled = true;
                        break;
                    }

                    // Export frame : first, dump data into color array
                    Color[] inputs;
                    if (frame.texture is Texture2D) // if using input frame
                    {
                        RenderTexture temp = RenderTexture.GetTemporary(frame.texture.width, frame.texture.height, 0, RenderTextureFormat.ARGBHalf);
                        Graphics.Blit((Texture2D)frame.texture, temp);
                        inputs = ReadBack(temp);
                    }
                    else // frame.texture is RenderTexture
                    {
                        frame.Process();
                        inputs = ReadBack((RenderTexture)frame.texture);
                    }

                    string fileName = GetNumberedFileName(path, i, frameCount);

                    // Dump data
                    byte[] bytes;

                    switch (exportSettings.exportMode)
                    {
                        case ExportMode.EXR:
#if UNITY_5_6_OR_NEWER
                            // New Exporter
                            {
                                Texture2D texture = new Texture2D(frame.texture.width, frame.texture.height, TextureFormat.RGBAHalf, settings.generateMipMaps, !settings.sRGB);
                                texture.SetPixels(inputs);
                                texture.Apply(true);
                                bytes = texture.EncodeToEXR();
                            }
#else
                        // Old Exporter
                        {
                            bytes = MiniEXR.MiniEXR.MiniEXRWrite((ushort)frame.texture.width, (ushort)frame.texture.height, settings.exportAlpha, inputs, true);
                        }
#endif
                            break;
                        case ExportMode.Targa:
                            {
                                bytes = MiniTGA.MiniTGAWrite((ushort)frame.texture.width, (ushort)frame.texture.height, settings.exportAlpha, inputs);
                            }
                            break;
                        case ExportMode.PNG:
                            {
                                Texture2D texture = new Texture2D(frame.texture.width, frame.texture.height, TextureFormat.RGBA32, settings.generateMipMaps, !settings.sRGB);
                                texture.SetPixels(inputs);
                                texture.Apply(true);
                                bytes = texture.EncodeToPNG();
                            }
                            break;
                        default:
                            {
                                bytes = new byte[0] { }; // Empty file that should not happen
                            }
                            break;
                    }
                    File.WriteAllBytes(fileName, bytes);

                    AssetDatabase.Refresh();

                    // Process Import if saved inside project
                    if (bIsInsideProject)
                    {
                        TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(fileName);
                        importer.wrapMode = exportSettings.wrapMode;
                        importer.filterMode = exportSettings.filterMode;
                        switch (exportSettings.dataContents)
                        {
                            case DataContents.Color:
                                importer.textureType = TextureImporterType.Default;
                                break;
                            case DataContents.NormalMap:
                                importer.textureType = TextureImporterType.NormalMap;
                                importer.convertToNormalmap = false;
                                break;
                            case DataContents.NormalMapFromGrayscale:
                                importer.textureType = TextureImporterType.NormalMap;
                                importer.convertToNormalmap = true;
                                break;
                            case DataContents.Sprite:
                                importer.textureType = TextureImporterType.Sprite;
                                importer.spriteImportMode = SpriteImportMode.Multiple;
                                UpdateSpriteMetaData(
	                                importer,
	                                frame,
	                                processingNodeStack
                                );
                                break;
                        }

                        TextureImporterSettings importerSettings = new TextureImporterSettings();
                        importer.ReadTextureSettings(importerSettings);

                        if (exportSettings.outputShape == OutputMode.Texture2DArray)
                        {
                            importerSettings.textureShape = TextureImporterShape.Texture2DArray;
                            importerSettings.flipbookColumns = processingNodeStack.outputSequence.numU;
                            importerSettings.flipbookRows = processingNodeStack.outputSequence.numV;
                        }
                        else if (exportSettings.outputShape == OutputMode.Texture2D)
                        {
                            importerSettings.textureShape = TextureImporterShape.Texture2D;
                        }

                        importer.SetTextureSettings(importerSettings);

                        importer.mipmapEnabled = exportSettings.generateMipMaps;

                        switch (exportSettings.exportMode)
                        {
                            case ExportMode.Targa:
                                importer.sRGBTexture = exportSettings.sRGB;
                                importer.alphaSource = exportSettings.exportAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                                importer.textureCompression = exportSettings.compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
                                break;
                            case ExportMode.EXR:
                                importer.sRGBTexture = false;
                                importer.alphaSource = (exportSettings.exportAlpha && !exportSettings.compress) ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                                importer.textureCompression = exportSettings.compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
                                break;
                            case ExportMode.PNG:
                                importer.sRGBTexture = exportSettings.sRGB;
                                importer.alphaSource = exportSettings.exportAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                                importer.textureCompression = exportSettings.compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
                                break;
                        }

                        AssetDatabase.ImportAsset(fileName, ImportAssetOptions.ForceUpdate);
                    }

                    // Separate Alpha
                    if (exportSettings.exportSeparateAlpha)
                    {
                        string alphaFilename = fileName.Substring(0, fileName.Length - 4) + "_alpha.tga";
                        // build alpha
                        for (int k = 0; k < inputs.Length; k++)
                        {
                            float a = inputs[k].a;
                            inputs[k] = new Color(a, a, a, a);
                        }
                        MiniTGA.MiniTGAWrite(alphaFilename, (ushort)frame.texture.width, (ushort)frame.texture.height, false, inputs);

                        AssetDatabase.Refresh();

                        // Process Importer for alpha if inside project
                        if (bIsInsideProject)
                        {
                            TextureImporter alphaImporter = (TextureImporter)TextureImporter.GetAtPath(alphaFilename);

                            if (exportSettings.dataContents == DataContents.Sprite)
                            {
                                alphaImporter.textureType = TextureImporterType.Sprite;
                                alphaImporter.spriteImportMode = SpriteImportMode.Multiple;
                                UpdateSpriteMetaData(
	                                alphaImporter,
	                                frame,
	                                processingNodeStack
                                );
                                alphaImporter.alphaSource = TextureImporterAlphaSource.None;
                            }
                            else
                            {
                                alphaImporter.textureType = TextureImporterType.SingleChannel;
                                alphaImporter.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                            }

                            alphaImporter.wrapMode = exportSettings.wrapMode;
                            alphaImporter.filterMode = exportSettings.filterMode;
                            alphaImporter.sRGBTexture = false;
                            alphaImporter.mipmapEnabled = exportSettings.generateMipMaps;
                            alphaImporter.textureCompression = exportSettings.compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;

                            AssetDatabase.ImportAsset(alphaFilename, ImportAssetOptions.ForceUpdate);
                        }
                    }

                    i++;
                }
            }
            catch(System.Exception e)
            {
                VFXToolboxGUIUtility.ClearProgressBar();
                Debug.LogException(e);
            }

            VFXToolboxGUIUtility.ClearProgressBar();

            if(bCanceled)
                return "";
            else
                return path;
        }

        public void UpdateExportedAssets(ProcessingNodeStack processingNodeStack)
        {
            if (ExportToFile(processingNodeStack, true) != "")
                exportSettings.frameCount = (ushort)processingNodeStack.outputSequence.frames.Count;
            else
                exportSettings.frameCount = 0;
        }

        private Color[] ReadBack(RenderTexture renderTexture)
        {
            Color[] inputs = VFXToolboxUtility.ReadBack(renderTexture);

            if(QualitySettings.activeColorSpace == ColorSpace.Linear && exportSettings.sRGB)
            {
                Color[] outputs = new Color[inputs.Length];
                for (int j = 0; j < inputs.Length; j++)
                {
                    outputs[j] = inputs[j].gamma;
                }
                return outputs;
            }
            return inputs;
        }

        private void UpdateSpriteMetaData(TextureImporter importer, ProcessingFrame frame, ProcessingNodeStack stack)
        {
			int numU = stack.outputSequence.numU;
			int numV = stack.outputSequence.numV;
			// TODO the input shouldn't be used here,  instead it should require a processing node
			// that stores the original sprites and requires the user set up a matching exporter that reads from it.
			// This same issue occurs in GetSpriteName.
			int length = stack.inputSequence.length;

			float width = (float)frame.texture.width / numU;
			float height = (float)frame.texture.height / numV;
	        
#if USES_SPRITE_DATA_PROVIDER
	        var factory = new SpriteDataProviderFactories();
	        factory.Init();
	        var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
	        dataProvider.InitSpriteEditorDataProvider();

	        Dictionary<string, SpriteRect> oldRectsByName = dataProvider.GetSpriteRects().ToDictionary(sr => sr.name, sr => sr);

	        var result = new SpriteRect[length];
#else
			SpriteMetaData[] result = new SpriteMetaData[length];
#endif

	        for (int j = 0; j < numV; j++)
	        for (int i = 0; i < numU; i++)
	        {
		        int index = i + j * numU;
		        if (index >= length)
			        break;
		        string spriteName = GetSpriteName(index, stack);
		        var rect = new Rect(i * width, j * height, width, height);
#if USES_SPRITE_DATA_PROVIDER
		        GUID guid = oldRectsByName.TryGetValue(spriteName, out var oldRect) ? oldRect.spriteID : new GUID();

		        SpriteRect data = new()
		        {
			        name = spriteName,
			        rect = rect,
			        spriteID = guid
		        };
#else
                var data = new SpriteMetaData
                {
	                name = spriteName,
	                rect = rect
                };
#endif
                result[index] = data;
	        }

	        
#if USES_SPRITE_DATA_PROVIDER
	        dataProvider.SetSpriteRects(result);
	        dataProvider.Apply();
#else
	        importer.spritesheet = result;
#endif
        }
		
		private string GetSpriteName(int index, ProcessingNodeStack stack)
		{
			switch (exportSettings.spriteNameFormat)
			{
				case SpriteNameFormat.FramePrefix:
					return $"Frame_{index}";
				case SpriteNameFormat.InputNames:
					// Note that input names requires the input sequence to be in the same order as the output.
					List<ProcessingFrame> frames = stack.inputSequence.frames;
					return index >= frames.Count ? index.ToString() : frames[index].texture.name;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}
}