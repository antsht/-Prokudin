using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
    {
        PrintUsage();
        return args.Length == 0 ? 1 : 0;
    }

    var positional = new List<string>();
    var output = string.Empty;
    var triptych = string.Empty;
    var triptychOrder = "rgb";
    var triptychOrderProvided = false;
    int? size = null;
    var reference = ChannelName.Green;
    var detector = "sift";
    var maxAlignIter = 3;
    var trimBorders = true;
    var sharpen = true;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o":
            case "--output":
                output = RequireValue(args, ref i);
                break;
            case "--triptych":
                triptych = RequireValue(args, ref i);
                break;
            case "--triptych-order":
                triptychOrder = RequireValue(args, ref i);
                triptychOrderProvided = true;
                break;
            case "--size":
                size = int.Parse(RequireValue(args, ref i));
                break;
            case "--reference":
                reference = ParseChannel(RequireValue(args, ref i));
                break;
            case "--detector":
                detector = RequireValue(args, ref i);
                break;
            case "--max-align-iter":
                maxAlignIter = int.Parse(RequireValue(args, ref i));
                break;
            case "--no-trim-borders":
                trimBorders = false;
                break;
            case "--no-sharpen":
                sharpen = false;
                break;
            case "reconstruct":
                break;
            default:
                positional.Add(args[i]);
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(output))
    {
        Console.Error.WriteLine("Error: --output is required.");
        return 1;
    }

    if (!string.IsNullOrWhiteSpace(triptych) && positional.Count > 0)
    {
        Console.Error.WriteLine("Error: --triptych cannot be combined with red/green/blue paths.");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(triptych) && positional.Count != 3)
    {
        Console.Error.WriteLine("Error: provide either --triptych PATH or red green blue paths.");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(triptych) && triptychOrderProvided)
    {
        Console.Error.WriteLine("Error: --triptych-order requires --triptych.");
        return 1;
    }

    var settings = new PipelineSettings
    {
        Align = new AlignOptions(reference, detector, maxAlignIter, trimBorders),
        OutputSize = size,
        Sharpen = sharpen,
    };

    try
    {
        if (!string.IsNullOrWhiteSpace(triptych))
        {
            ValidateInput(triptych);
            await ReconstructionPipeline.ReconstructFromTriptychAsync(
                triptych,
                TriptychSplitter.ParseOrder(triptychOrder),
                output,
                settings);
        }
        else
        {
            foreach (var path in positional)
            {
                ValidateInput(path);
            }

            await ReconstructionPipeline.ReconstructFromPathsAsync(positional[0], positional[1], positional[2], output, settings);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    return 0;
}

static string RequireValue(string[] args, ref int index)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"Missing value for {args[index]}.");
    }

    index++;
    return args[index];
}

static ChannelName ParseChannel(string value)
{
    return value.ToLowerInvariant() switch
    {
        "red" => ChannelName.Red,
        "green" => ChannelName.Green,
        "blue" => ChannelName.Blue,
        _ => throw new ArgumentException("Reference must be red, green, or blue."),
    };
}

static void ValidateInput(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"file not found: {path}", path);
    }

    if (!ImageLoader.IsSupportedImagePath(path))
    {
        throw new NotSupportedException($"unsupported format for {path}. Use PNG, JPEG (.jpg/.jpeg), or TIFF (.tif/.tiff).");
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Prokudin.Cli reconstruct red.png green.png blue.png -o output.png [--size N] [--reference green] [--detector sift|orb]");
    Console.WriteLine("  Prokudin.Cli reconstruct --triptych scan.tif --triptych-order rgb|bgr -o output.png");
}
