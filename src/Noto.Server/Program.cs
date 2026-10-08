using Noto.Server;
using Noto.Server.Config;

// .env is read here, not in ServerHost. The test host runs this entry point too, so it sets NOTO_NO_DOTENV.
DotEnv.LoadNearest(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
ServerHost.Build(args).Run();

public partial class Program;
