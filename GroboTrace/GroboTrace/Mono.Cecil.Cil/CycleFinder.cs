using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GroboTrace.Mono.Cecil.Cil
{
    class CycleFinder
    {
        private void dfs(Instruction x)
        {
            color[x] = Colors.Grey;
            
            if (x.OpCode != OpCodes.Br && x.OpCode != OpCodes.Br_S)
            {
                var target = x.Next;

                if (target != null)
                {
                    if (color[target] == Colors.Grey)
                    {
                        containsCycles = true;
                    }

                    if (color[target] == Colors.White)
                    {
                        dfs(target);
                    }
                }

            }

            if (x.OpCode.OperandType == OperandType.InlineBrTarget || x.OpCode.OperandType == OperandType.ShortInlineBrTarget)
            {
                var target = (Instruction)x.Operand;

                if (target != null)
                {
                    if (color[target] == Colors.Grey)
                    {
                        containsCycles = true;
                    }

                    if (color[target] == Colors.White)
                    {
                        dfs(target);
                    }
                }

            }

            color[x] = Colors.Black;
        }


        public CycleFinder(Instruction[] instructions)
        {
            this.instructions = instructions;

            foreach (var instruction in instructions)
            {
                color[instruction] = Colors.White;
            }
            
            dfs(instructions[0]);
        }

        public bool IsThereAnyCycles()
        {
            return containsCycles;
        }

        private enum Colors
        {
            White = 0, // unvisited vertex
            Grey = 1, // in process
            Black = 2 // finished
        }


        private bool containsCycles;
        private Instruction[] instructions;
        private Dictionary<Instruction, Colors> color = new Dictionary<Instruction, Colors>();
    }
}
