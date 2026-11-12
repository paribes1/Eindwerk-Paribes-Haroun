using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Rendering;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eindwerk.PlayerMechanics.Editor
{
    /// <summary>Only explicit headless network test builds omit rendering code.</summary>
    public sealed class HeadlessSmokeShaderStripper : IPreprocessShaders, IPreprocessComputeShaders
    {
        public int callbackOrder => int.MaxValue;
        private static bool IsHeadlessTest => Environment.GetCommandLineArgs().Contains("-mechanicsHeadlessBuild");

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (IsHeadlessTest) data.Clear();
        }

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (IsHeadlessTest) data.Clear();
        }
    }
}
