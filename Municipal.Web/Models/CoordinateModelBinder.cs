using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Municipal.Web.Models;

public class CoordinateModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        if (string.IsNullOrWhiteSpace(value.FirstValue))
            context.Result = ModelBindingResult.Success(null);
        else if (double.TryParse(value.FirstValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate) && double.IsFinite(coordinate))
            context.Result = ModelBindingResult.Success(coordinate);
        else
            context.ModelState.TryAddModelError(context.ModelName, "Invalid GPS coordinate.");
        return Task.CompletedTask;
    }
}
