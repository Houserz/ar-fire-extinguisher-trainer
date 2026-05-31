using UnityEngine;

namespace FireExtinguisherTrainer
{
    public static class VrStableParticleVisuals
    {
        private const string ParticleMaterialName = "VR_Stable_Mesh_Particle";
        private static Mesh fallbackMesh;

        public static void ConfigureMeshParticleRenderer(ParticleSystem particleSystem, string preferredMeshName)
        {
            if (particleSystem == null)
            {
                return;
            }

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                renderer = particleSystem.gameObject.AddComponent<ParticleSystemRenderer>();
            }

            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = LoadParticleMesh(preferredMeshName);
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.allowRoll = false;
            renderer.enableGPUInstancing = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            if (renderer.sharedMaterial == null)
            {
                renderer.sharedMaterial = CreateParticleMaterial();
            }
        }

        private static Mesh LoadParticleMesh(string preferredMeshName)
        {
            Mesh mesh = !string.IsNullOrEmpty(preferredMeshName)
                ? Resources.GetBuiltinResource<Mesh>(preferredMeshName)
                : null;

            mesh ??= Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            mesh ??= Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            mesh ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return mesh != null ? mesh : CreateFallbackMesh();
        }

        private static Mesh CreateFallbackMesh()
        {
            if (fallbackMesh != null)
            {
                return fallbackMesh;
            }

            fallbackMesh = new Mesh
            {
                name = "VR Stable Particle Fallback Mesh",
                hideFlags = HideFlags.DontSave,
            };

            fallbackMesh.vertices = new[]
            {
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0f, 0f, -0.5f),
                new Vector3(0f, -0.5f, 0f),
            };
            fallbackMesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 1,
                5, 2, 1,
                5, 3, 2,
                5, 4, 3,
                5, 1, 4,
            };
            fallbackMesh.RecalculateNormals();
            fallbackMesh.RecalculateBounds();
            return fallbackMesh;
        }

        private static Material CreateParticleMaterial()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = ParticleMaterialName,
                color = Color.white,
                hideFlags = HideFlags.DontSave,
                renderQueue = 3000,
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            return material;
        }
    }
}
