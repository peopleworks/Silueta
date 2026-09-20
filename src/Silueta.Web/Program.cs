using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Silueta.Web;

// The whole application is one page and one library. There is deliberately no HttpClient registered
// here: everything the demo needs is compiled into the assembly the browser already downloaded — the
// pattern pack, the lineage, the surrogate pools, and the measured leak rate. A transcript pasted into
// this page has nowhere to go, and that is the claim the page exists to make.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
