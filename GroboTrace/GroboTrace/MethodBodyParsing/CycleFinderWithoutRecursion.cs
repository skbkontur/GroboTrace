using System.Collections.Generic;
using System.Linq;

namespace GroboTrace.MethodBodyParsing
{
    internal static class CycleFinderWithoutRecursion
    {
        public static bool HasCycle(Instruction[] instructions)
        {
            var color = new Dictionary<Instruction, Colors>();
            foreach(var instruction in instructions)
                color[instruction] = Colors.White;
            var stack = new Stack<State>();
            stack.Push(new State {instr = instructions[0], s = 0});
            while(stack.Count > 0)
            {
                var peek = stack.Peek();
                var x = peek.instr;
                if(peek.s == 0)
                {
                    peek.s = 1;
                    color[x] = Colors.Grey;

                    if (!stopInstructions.Contains(x.OpCode))
                    {
                        var target = x.Next;

                        if (target != null)
                        {
                            if(color[target] == Colors.Grey)
                                return true;

                            if (color[target] == Colors.White)
                            {
                                stack.Push(new State {instr = target, s = 0});
                                continue;
                            }
                        }
                    }
                }

                if(peek.s == 1)
                {
                    peek.s = 2;
                    if(x.OpCode.OperandType == OperandType.InlineBrTarget || x.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                    {
                        var target = (Instruction)x.Operand;

                        if(target != null)
                        {
                            if(color[target] == Colors.Grey)
                                return true;

                            if(color[target] == Colors.White)
                            {
                                stack.Push(new State {instr = target, s = 0});
                                continue;
                            }
                        }

                    }
                }

                if(peek.s == 2)
                {
                    color[x] = Colors.Black;
                    stack.Pop();
                }
            }
            return false;
        }

        private class State
        {
            public Instruction instr;
            public int s;
        }

        private enum Colors
        {
            White = 0, // unvisited vertex
            Grey = 1, // in process
            Black = 2 // finished
        }

        private static OpCode[] stopInstructions = new[]
        {
            OpCodes.Br,
            OpCodes.Br_S,
            OpCodes.Ret,
            OpCodes.Throw,
            OpCodes.Rethrow,
            OpCodes.Jmp,
            OpCodes.Leave,
            OpCodes.Leave_S
        };
    }
}