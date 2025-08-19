using eShop.CryptoPayment.API.Apis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();

var cryptoPayment = app.NewVersionedApi("CryptoPayment");

cryptoPayment.MapCryptoPaymentApiV1()
           .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();