using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.ModelBinding;

namespace jqGridExtension
{
    public class jqGridExtension
    {
        public static GridModel ApplyJqGridFilters<T>(IQueryable<T> model, GridSettings grid) where T: class
        {
            //filtering
            if (grid.IsSearch)
                model = model.Where<T>(grid.Where);

            //sorting
            if (string.IsNullOrEmpty(grid.SortColumn))
                grid.SortColumn = "id";

            model = model.OrderBy<T>(grid.SortColumn, grid.SortOrder);

            //paging
            if (grid.PageIndex == 0)
                grid.PageIndex = 1;

            if (grid.PageSize == 0)
                grid.PageSize = 10;

            var data = model.Skip((grid.PageIndex - 1) * grid.PageSize).Take(grid.PageSize).ToArray();

            //count
            var count = model.Count();

            //converting in grid format
            var gridmodel = new GridModel()
            {
                total = (int)Math.Ceiling((double)count / grid.PageSize),
                page = grid.PageIndex,
                records = count,
                rows = model.ToArray()
            };

            return gridmodel;
        }
    }
    
    public class GridModelBinder : IModelBinder
    {
        public bool BindModel(HttpActionContext actionContext, ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(GridSettings))
                return false;

            //var contentFromInputStream = new StreamReader((actionContext.ControllerContext.Request.Properties["MS_HttpContext"] as System.Web.HttpContextWrapper).Request.InputStream).ReadToEnd();
            var request = actionContext.Request.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrEmpty(request))
                return false;

            var qscoll = HttpUtility.ParseQueryString(request);
            try
            {
                string filters = qscoll["filters"];
                if (string.IsNullOrEmpty(filters))
                    filters = string.Format("{{\"groupOp\":\"AND\",\"rules\":[{{\"field\":\"{0}\",\"op\":\"{1}\",\"data\":\"{2}\"}}]}}",qscoll["searchField"], qscoll["searchOper"], qscoll["searchString"]);

                bindingContext.Model = new GridSettings()
                {
                    IsSearch = bool.Parse(qscoll["_search"] ?? "false"),
                    PageIndex = int.Parse(qscoll["page"] ?? "1"),
                    PageSize = int.Parse(qscoll["rows"] ?? "10"),
                    SortColumn = qscoll["sidx"] ?? "",
                    SortOrder = qscoll["sord"] ?? "asc",
                    Where = Filter.Create(filters),
                    client_id = int.Parse(qscoll["client_id"] ?? "0"),
                    request_id = int.Parse(qscoll["request_id"] ?? "0")
                };
                return true;
            }
            catch(Exception ex)
            {
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, ex.ToString());
                return false;
            }
        }

        private T GetValue<T>(ModelBindingContext bindingContext, string key, T defaulValue)
        {
            var valueResult = bindingContext.ValueProvider.GetValue(key);
            if (valueResult != null)
            {
                bindingContext.ModelState.SetModelValue(key, valueResult);
                return (T)valueResult.ConvertTo(typeof(T));
            }
            else
                return defaulValue;
        }  
    }

    public class GridModelBinderProvider : ModelBinderProvider
    {
        public override IModelBinder GetBinder(HttpConfiguration configuration, Type modelType)
        {
            return new GridModelBinder();
        }
    }

    [ModelBinder(typeof(GridModelBinderProvider))]
    public class GridSettings
    {
        public int client_id { get; set; }
        public int request_id { get; set; }
        public bool IsSearch { get; set; }
        public int PageSize { get; set; }
        public int PageIndex { get; set; }
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }

        public Filter Where { get; set; }
    }

    public class Filter
    {
        public string groupOp { get; set; }
        public Rule[] rules { get; set; }

        public static Filter Create(string jsonData)
        {
            try
            {
                var objData = Newtonsoft.Json.JsonConvert.DeserializeObject<Filter>(jsonData);
                return objData;
            }
            catch
            {
                return null;
            }
        }
    }

    public class Rule
    {
        public string field { get; set; }
        public string op { get; set; }
        public string data { get; set; }
    }

    public class GridModel
    {
        public int total;
        public int page;
        public int records;
        public dynamic[] rows;
    }

    public static class DynamicLinqHelper
    {
        public static IQueryable<T> OrderBy<T>(this IQueryable<T> query, string sortColumn, string direction)
        {
            if (string.IsNullOrEmpty(sortColumn))
                return query;
            
            string methodName = string.Format("OrderBy{0}", string.IsNullOrEmpty(direction) || direction.ToLower() == "asc" ? "" : "Descending");
            ParameterExpression parameter = Expression.Parameter(query.ElementType, "p");

            MemberExpression memberAccess = null;
            foreach (var property in sortColumn.Split('.'))
                memberAccess = MemberExpression.Property(memberAccess ?? (parameter as Expression), property);

            LambdaExpression orderByLambda = Expression.Lambda(memberAccess, parameter);

            MethodCallExpression result = Expression.Call(
                      typeof(Queryable),
                      methodName,
                      new[] { query.ElementType, memberAccess.Type },
                      query.Expression,
                      Expression.Quote(orderByLambda));

            return query.Provider.CreateQuery<T>(result);
        }

        public static IQueryable<T> Where<T>(this IQueryable<T> source, Filter gridfilter)
        {
            Expression resultCondition = null;
            ParameterExpression parameter = Expression.Parameter(source.ElementType, "p");

            foreach (var rule in gridfilter.rules)
            {
                if (string.IsNullOrEmpty(rule.field)) continue;
            
                Expression memberAccess = null;
                foreach (var property in rule.field.Split('.'))
                    memberAccess = MemberExpression.Property(memberAccess ?? (parameter as Expression), property);

                //change param value type - necessary to getting bool from string
                Type t; 
                object value = null;

                if (memberAccess.Type.Namespace.StartsWith("MvcApplication6"))
                {
                    memberAccess = MemberExpression.Property(memberAccess, "Id");
                    t = memberAccess.Type;
                    if (rule.data == "-1") continue;
                }
                else
                {
                    t = Nullable.GetUnderlyingType(memberAccess.Type) ?? memberAccess.Type;
                }

                try
                {
                    value = (rule.data == null) ? null : Convert.ChangeType(rule.data, t);
                }
                catch (FormatException)
                {
                    value = rule.data;
                    memberAccess = Expression.Call(memberAccess, memberAccess.Type.GetMethod("ToString", System.Type.EmptyTypes));
                }
            
                ConstantExpression filter = Expression.Constant(value);
                ConstantExpression nullfilter = Expression.Constant(null);

                //switch operation
                Expression toLower = null;
                Expression condition = null;
                switch (rule.op)
                {
                    case "eq": //equal
                        if (value is string)
                        {
                            toLower = Expression.Call(memberAccess, typeof(string).GetMethod("ToLower", System.Type.EmptyTypes));
                            condition = Expression.Equal(toLower, Expression.Constant(value.ToString().ToLower()));
                        }
                        else
                            condition = Expression.Equal(memberAccess, filter);
                        
                        break;
                    case "ne"://not equal
                        condition = Expression.NotEqual(memberAccess, filter);
                        break;
                    case "lt": //less than
                        condition = Expression.LessThan(memberAccess, filter);
                        break;
                    case "le"://less than or equal
                        condition = Expression.LessThanOrEqual(memberAccess, filter);
                        break;
                    case "gt": //greater than
                        condition = Expression.GreaterThan(memberAccess, filter);
                        break;
                    case "ge"://greater than or equal
                        condition = Expression.GreaterThanOrEqual(memberAccess, filter);
                        break;
                    case "bw": //begins with
                        toLower = Expression.Call(memberAccess, typeof(string).GetMethod("ToLower", System.Type.EmptyTypes));
                        condition = Expression.Call(toLower, typeof(string).GetMethod("StartsWith", new[] { typeof(string) }), Expression.Constant(value));
                        break;
                    case "bn": //doesn"t begin with

*** The rest of the content is truncated. ***