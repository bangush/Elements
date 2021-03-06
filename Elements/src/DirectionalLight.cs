using System;
using Elements.Geometry;

namespace Elements
{
    /// <summary>
    /// A directional light.
    /// </summary>
    /// <example>
    /// [!code-csharp[Main](../../Elements/test/LightTests.cs?name=example)]
    /// </example>
    public class DirectionalLight : Element
    {
        /// <summary>
        /// The color of the light.
        /// The color's alpha value will be ignored.
        /// </summary>
        public Color Color { get; set; }

        /// <summary>
        /// The intensity of the light measured in lux.
        /// </summary>
        public double Intensity { get; set; }

        /// <summary>
        /// The light's transform.
        /// The light will be aimed along the transform's -Z axis.
        /// </summary>
        public Transform Transform { get; set; }

        /// <summary>
        /// Create a directional light.
        /// </summary>
        /// <param name="color">The color of the light.</param>
        /// <param name="intensity">The intensity of the light measured in lux.</param>
        /// <param name="transform">The light's transform.</param>
        /// <param name="id">The unique identifier of the light.</param>
        /// <param name="name">The name of the light.</param>
        /// <returns></returns>
        public DirectionalLight(Color color,
                                Transform transform,
                                double intensity = 1.0,
                                Guid id = default(Guid),
                                string name = "Sun") : base(id != default(Guid) ? id : Guid.NewGuid(),
                                                            name)
        {
            this.Color = color;
            this.Intensity = intensity;
            this.Transform = transform;
        }
    }
}