using System.Runtime.InteropServices;

namespace AwakeBuddy.Power;

public sealed class InputKeepAlive
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    public bool Pulse()
    {
        Input[] inputs =
        [
            CreateMoveInput(dx: 1, dy: 0),
            CreateMoveInput(dx: -1, dy: 0)
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == (uint)inputs.Length;
    }

    private static Input CreateMoveInput(int dx, int dy)
    {
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = 0,
                    Flags = MouseEventMove,
                    Time = 0,
                    ExtraInfo = 0
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
