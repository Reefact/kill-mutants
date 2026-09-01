using Microsoft.CodeAnalysis;

namespace Sample.Generator
{
    /// <summary>
    /// Contributes a small class the library depends on. The generated body calls into
    /// Sample.Generator.Support, so the generator cannot run unless its own dependency loads.
    /// </summary>
    [Generator]
    public class LimitsGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
            {
                string name = Support.Naming.PropertyFor("adultAge");

                ctx.AddSource(
                    "Limits.g.cs",
                    "namespace Sample.Library\n" +
                    "{\n" +
                    "    public static class Limits\n" +
                    "    {\n" +
                    "        public static int " + name + " { get { return 18; } }\n" +
                    "    }\n" +
                    "}\n");
            });
        }
    }
}
