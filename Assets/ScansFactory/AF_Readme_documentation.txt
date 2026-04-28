Thank you for supporting our work!

This project includes support for all Unity render pipelines, organized into separate folders. By default, it uses HDRP, but switching to URP or Built-in is quick and easy.

🔄 To switch the render pipeline:
Project Settings → Graphics
Set Default Render Pipeline to:
• AF_Universal... for URP
• None for Built-in

Project Settings → Quality
Set the Render Pipeline Asset to match the above (if needed).

Open the demo scene from the appropriate folder:
• AbandonedFactory_URP/Demo for URP
• AbandonedFactory/Demo for Built-in

🛠️ Troubleshooting:
URP looks too dark or too bright?
Try toggling the Sky and Fog Volume on or off.

Built-in looks too dark?
Install the free Unity Post-Processing package.
Then:
• Add a Post-Processing Layer to the Main Camera
• Assign a matching Post-Processing Volume
(Full setup guide with images is in our documentation.)

Reflections missing or missmatch?
• Rebuild reflection probes.

You can safely delete any folders you’re not using — except the Common folder, which contains shared resources (meshes, textures, etc.).

If you enjoy the asset, we’d be incredibly grateful for a quick review — it helps us keep creating more content for you!

Have questions or need help? Reach out any time: support@scansfactory.com

— Scans Factory Team