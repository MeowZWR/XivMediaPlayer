using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace XivMediaPlayer {
    public unsafe class ScratchTest {
        public static void Test() {
            var time = Framework.Instance()->UtcTime;
            System.Console.WriteLine(time);
        }
    }
}
