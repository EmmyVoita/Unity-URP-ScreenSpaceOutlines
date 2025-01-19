# ScreenSpaceOutlines
This project is an enhancement of Robin Seibold's screen space outlines implementation, which I expanded upon to meet the requirements of various projects I was working on. These enhancements included improving edge detection using non-maximum suppression and adding anti-aliasing. I initially worked on this project using Unity version 2022.3.50f1, but I encountered issues with setting multiple render targets, which I needed for implementing a Temporal Anti-Aliasing (TAA) shader. To diagnose bugs, I relied heavily on RenderDoc and eventually decided to switch to Unity 6.0 to use the Render Graph system.

The included zip file contains a Unity 60000.0.32f1 project that includes everything needed for the screen space outlines, including a sample scene and the "PC_Renderer" Universal Renderer Data with the screen space outlines effect applied. The unity package "ScreenSpaceOutlines.unitypackage" is just everything except the URP settings asset. You can add "SSO Renderer Feature" to your Universal Renderer Data asset.

**Example Scene:**

![2025-01-1915-50-26-ezgif com-optimize](https://github.com/user-attachments/assets/596df888-3ee6-4d8a-b31a-f181cd75a60d)


**Non-maximum supression (NSM) to resolve very steep (view-normal) angle transitions:**

NMS is applied as an optional step after calculating edge detection, which can be enabled or disabled using the "Use NMS" bool in the renderer feature settings.

Here is an example of the artifact when there is no NMS applied (left), and the issue resolved with NMS applied (right). My NMS implementation is not perfect. It can decrease the sharpness of outlines, which is also clear in the following image. I plan on reworking this at some point in the future when I have better knowledge of how to solve the problem. I will also add support for multiple different outline colors eventually.  

![NMS_1](https://github.com/user-attachments/assets/09fd89b1-6e21-420b-8960-80e9d45cd1cd)

**Temporal Anti-Aliasing** 

This project implements playdeadgames temporal reprojection solution in URP. It is an optional anti-aliasing pass after applying screen space outines that applies TAA to the entire image, since Unity's built-in anti-aliasing functions don't apply to the additional injected passes.

If you are interested you can read more about the project here:
[come back and insert link]



**Links**
- https://www.youtube.com/watch?v=LMqio9NsqmM&ab_channel=RobinSeibold
- https://github.com/Robinseibold/Unity-URP-Outlines
- https://gdcvault.com/play/1022970/Temporal-Reprojection-Anti-Aliasing-in
- https://github.com/playdeadgames/temporal
