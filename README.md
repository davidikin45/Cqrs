# CQRS = Command and Query Responsibility Segregation

## About CQRS
* Command = POST, PUT, DELETE = Produces sude effects, returns void or id of entity
* Query = GET = Side-effect free, returns non-void
* 2 domain models
* Moves away from CRUD based domain models
* Experts don't speak in CRUD terms. Lack of ubiquitous language.
* Have domain expert and programmers speak the same language!
* Break Create/Update down into more meaningful operations otherwise UI & API also becomes CRUD.
* The opposite of CRUD is task-based interface.
* Avoid DTOs that don't use fields in 100% of cases.
* Command is a serializable method call.
* Command Handler is like a .NET controller with a single method.
* Commands should use the ubiquitous language.
* Commands and Events are part of the Core Domain Model
* The use of commands as DTOs = Entities as DTOs
* DTOS = Backwards compatibility. Only needed if have multiple clients and dont want to break an old version of the Api.
* Commands = Actions within the Application
* Queries should return DTOs
* Use controller for ASP.NET wiring only
* A decorator is a class or a method that modifies the behavior of an existing class or method without changing it's public interface
* Don't allow command handlers to raise additional commands. Reuse code by putting it into domain model.
* Cqrs can provide alot of benefits without event sourcing.

## Advantages
1. Scalability
2. Performance
3. Simplicity

## Encapsulating an Entity
```
public class Entity
{
	protected Entity()
	{
	}

	public Entity(string name)
	:this()
	{
		Name = name
	}

	public string Name { get; private set; }

	 private readonly List<Role> _list = new List<Role>();
        public virtual IReadOnlyList<Role> List => _list.AsReadOnly();
}
```

## Messages
1. Commands = Tell the application to do something. Imperative tense. Post-fix with "Command". Push model.
2. Queries = Ask the application about something. Start with word "Get". Post-fix with "Query".
3. Events = Inform external applications. Paste tense. Post-fix with "Event". Pull model.

## Onion Architecture
![alt text](img/onion.jpg "Onion Architecture")

## Microservice Architecture
![alt text](img/architecture.jpg "Software Architecture")
Microservice architecture aims to make each backends part independent by giving it:
* Autonomous features
* Team Ownership (Team take ownership of frontend, backend and storage rather than becoming and expert at only front or backend part.)
* Tech agnostic (Instead of investing in a particular tech for entire application can change to different technologies that suit the particular problem.)
* User experience
* Value driven (Customer value driven meaning every change can give the customer value.)
* Microservices driven

* The apis don't necessarily need to be in seperate projects but Mvc application parts allow the apis to be developed independently and then merged back together for a single deployment.
* The storage for each module doesn't necessarily need to be a seperate physical database. A seperate DbContext (Bounded Context) is a good option.
* The important part is to maintain and think about each application module as being independent.
* Microservice frontend architecture aim to have multiple micro frontend parts that are independent, consistent and coherent. This can be acheieved with Micro Apps, iframes, Web Components, Framework Based Components, Server Side Transclusion and Client Side Transclusion. 

## Commands
```
public interface ICommand
{
}
```
```
public interface ICommandHandler<TCommand>
where TCommand : ICommand
{
    Result Handle(TCommand command);
}
```
```
public sealed class EditPersonalInfoCommand : ICommand
{
	public long Id { get; }
	public string Name { get; }
	public string Email { get; }

	public EditPersonalInfoCommand(long id, string name, string email)
	{
		Id = id;
		Name = name;
		Email = email;
	}

	[AuditLog]
	[DatabaseRetry]
	internal sealed class EditPersonalInfoCommandHandler : ICommandHandler<EditPersonalInfoCommand>
	{
		private readonly SessionFactory _sessionFactory;

		public EditPersonalInfoCommandHandler(SessionFactory sessionFactory)
		{
			_sessionFactory = sessionFactory;
		}

		public Result Handle(EditPersonalInfoCommand command)
		{
			var unitOfWork = new UnitOfWork(_sessionFactory);
			var repository = new StudentRepository(unitOfWork);
			Student student = repository.GetById(command.Id);

			if (student == null)
				return Result.Fail($"No student found for Id {command.Id}");

			student.Name = command.Name;
			student.Email = command.Email;

			unitOfWork.Commit();

			return Result.Ok();
		}
	}
}
```

## Queries
```
public interface IQuery<TResult>
{
}
```
```
public interface IQueryHandler<TQuery, TResult>
where TQuery : IQuery<TResult>
{
	TResult Handle(TQuery query);
}
```
```
public sealed class GetListQuery : IQuery<List<StudentDto>>
{
	public string EnrolledIn { get; }
	public int? NumberOfCourses { get; }

	public GetListQuery(string enrolledIn, int? numberOfCourses)
	{
		EnrolledIn = enrolledIn;
		NumberOfCourses = numberOfCourses;
	}

	internal sealed class GetListQueryHandler : IQueryHandler<GetListQuery, List<StudentDto>>
	{
		private readonly QueriesConnectionString _connectionString;

		public GetListQueryHandler(QueriesConnectionString connectionString)
		{
			_connectionString = connectionString;
		}

		public Task<List<StudentDto>> HandleAsync(GetListQuery query)
		{
			string sql = @"
				SELECT s.StudentID Id, s.Name, s.Email,
					s.FirstCourseName Course1, s.FirstCourseCredits Course1Credits, s.FirstCourseGrade Course1Grade,
					s.SecondCourseName Course2, s.SecondCourseCredits Course2Credits, s.SecondCourseGrade Course2Grade
				FROM dbo.Student s
				WHERE (s.FirstCourseName = @Course
						OR s.SecondCourseName = @Course
						OR @Course IS NULL)
					AND (s.NumberOfEnrollments = @Number
						OR @Number IS NULL)
				ORDER BY s.StudentID ASC";

			using (SqlConnection connection = new SqlConnection(_connectionString.Value))
			{
				List<StudentDto> students = connection
					.Query<StudentDto>(sql, new
					{
						Course = query.EnrolledIn,
						Number = query.NumberOfCourses
					})
					.ToList();

				return students;
			}
		}
	}
}
```

## Dispatcher
```
public interface ICqrsDispatcher
{
    Task<Result> DispatchAsync(ICommand command);
    Task<Result<T>> DispatchAsync<T>(ICommand<T> command);

    Task<T> DispatchAsync<T>(IQuery<T> query);
}
```
```
public sealed class CqrsDispatcher : ICqrsDispatcher
{
	private readonly IServiceProvider _provider;

	public CqrsDispatcher(IServiceProvider provider)
	{
		_provider = provider;
	}

	public async Task<Result> DispatchAsync(ICommand command)
	{
		Type type = typeof(ICommandHandler<>);
		Type[] typeArgs = { command.GetType() };
		Type handlerType = type.MakeGenericType(typeArgs);

		dynamic handler = _provider.GetService(handlerType);
		Result result = await handler.HandleAsync((dynamic)command);
		return result;
	}

	public async Task<Result<T>> DispatchAsync<T>(ICommand<T> command)
	{
		Type type = typeof(ICommandHandler<,>);
		Type[] typeArgs = { command.GetType(), typeof(T) };
		Type handlerType = type.MakeGenericType(typeArgs);

		dynamic handler = _provider.GetService(handlerType);
		Result<T> result = await handler.HandleAsync((dynamic)command);

		return result;
	}

	public async Task<T> DispatchAsync<T>(IQuery<T> query)
	{
		Type type = typeof(IQueryHandler<,>);
		Type[] typeArgs = { query.GetType(), typeof(T) };
		Type handlerType = type.MakeGenericType(typeArgs);

		dynamic handler = _provider.GetService(handlerType);
		T result = await handler.HandleAsync((dynamic)query);

		return result;
	}
}
```

## Service Collection Extensions
```
public static class CqrsServiceCollectionExtensions
{
	public static void AddCqrs(this IServiceCollection services)
	{
		services.AddCqrsDispatcher();
		services.AddCqrsHandlers(new List<Assembly>() { Assembly.GetCallingAssembly() });
	}

	public static void AddCqrs(this IServiceCollection services, IEnumerable<Assembly> assemblies)
	{
		services.AddCqrsDispatcher();
		services.AddCqrsHandlers(assemblies);
	}

	public static void AddCqrsDispatcher(this IServiceCollection services)
    {
            services.AddTransient<ICqrsDispatcher, CqrsDispatcher>();
	}

	public static void AddCqrsHandlers(this IServiceCollection services, IEnumerable<Assembly> assemblies)
	{
		List<Type> handlerTypes = assemblies.SelectMany(assembly => assembly.GetTypes())
			.Where(x => x.GetInterfaces().Any(y => IsHandlerInterface(y)))
			.Where(x => x.Name.EndsWith("Handler"))
			.ToList();

		foreach (Type type in handlerTypes)
		{
			AddHandler(services, type);
		}
	}

	private static void AddHandler(IServiceCollection services, Type type)
	{
		object[] attributes = type.GetCustomAttributes(false);

		List<Type> pipeline = attributes
			.Select(x => ToDecorator(x))
			.Concat(new[] { type })
			.Reverse()
			.ToList();

		Type interfaceType = type.GetInterfaces().Single(y => IsHandlerInterface(y));
		Func<IServiceProvider, object> factory = BuildPipeline(pipeline, interfaceType);

		services.AddTransient(interfaceType, factory);
	}

	private static Func<IServiceProvider, object> BuildPipeline(List<Type> pipeline, Type interfaceType)
	{
		List<ConstructorInfo> ctors = pipeline
			.Select(x =>
			{
				Type type = x.IsGenericType ? x.MakeGenericType(interfaceType.GenericTypeArguments) : x;
				return type.GetConstructors().Single();
			})
			.ToList();

		Func<IServiceProvider, object> func = provider =>
		{
			object current = null;

			foreach (ConstructorInfo ctor in ctors)
			{
				List<ParameterInfo> parameterInfos = ctor.GetParameters().ToList();

				object[] parameters = GetParameters(parameterInfos, current, provider);

				current = ctor.Invoke(parameters);
			}

			return current;
		};

		return func;
	}

	private static object[] GetParameters(List<ParameterInfo> parameterInfos, object current, IServiceProvider provider)
	{
		var result = new object[parameterInfos.Count];

		for (int i = 0; i < parameterInfos.Count; i++)
		{
			result[i] = GetParameter(parameterInfos[i], current, provider);
		}

		return result;
	}

	private static object GetParameter(ParameterInfo parameterInfo, object current, IServiceProvider provider)
	{
		Type parameterType = parameterInfo.ParameterType;

		if (IsHandlerInterface(parameterType))
			return current;

		object service = provider.GetService(parameterType);
		if (service != null)
			return service;

		throw new ArgumentException($"Type {parameterType} not found");
	}

	private static Type ToDecorator(object attribute)
	{
		Type type = attribute.GetType();

		if (type == typeof(DatabaseRetryAttribute))
			return typeof(DatabaseRetryDecorator<>);

		if (type == typeof(AuditLogAttribute))
			return typeof(AuditLoggingDecorator<>);

		// other attributes go here

		throw new ArgumentException(attribute.ToString());
	}

	private static bool IsHandlerInterface(Type type)
	{
		if (!type.IsGenericType)
			return false;

		Type typeDefinition = type.GetGenericTypeDefinition();

		return typeDefinition == typeof(ICommandHandler<>) || typeDefinition == typeof(IQueryHandler<,>);
	}
}
```

## Decorators
1. Introduce cross-cutting concerns
2. Avoid code duplication
3. Adhering to the single responsibility principle.

* Same pattern that ASP.NET middleware uses
* Controllers similar to Handlers

### Db Retry Decorator
```
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class DatabaseRetryAttribute : Attribute
{
    public DatabaseRetryAttribute()
    {
    }
}
```
```
public sealed class DatabaseRetryDecorator<TCommand> : ICommandHandler<TCommand>
	where TCommand : ICommand
{
	private readonly ICommandHandler<TCommand> _handler;
	private readonly AppSettings _appSettings;

	public DatabaseRetryDecorator(ICommandHandler<TCommand> handler, AppSettings appSettings)
	{
		_appSettings = appSettings;
		_handler = handler;
	}

	public async Task<Result> HandleAsync(TCommand command)
	{
		for (int i = 0; ; i++)
		{
			try
			{
				Result result = await _handler.HandleAsync(command);
				return result;
			}
			catch (Exception ex)
			{
				if (i >= _appSettings.NumberOfDatabaseRetries || !IsDatabaseException(ex))
					throw;
			}
		}
	}

	private bool IsDatabaseException(Exception exception)
	{
		string message = exception.InnerException?.Message;

		if (message == null)
			return false;

		return message.Contains("The connection is broken and recovery is not possible")
			|| message.Contains("error occurred while establishing a connection");
	}
}
```

### Audit Logging Decorator
```
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class AuditLogAttribute : Attribute
{
    public AuditLogAttribute()
    {
    }
}
```
```
public sealed class AuditLoggingDecorator<TCommand> : ICommandHandler<TCommand>
	where TCommand : ICommand
{
	private readonly ICommandHandler<TCommand> _handler;

	public AuditLoggingDecorator(ICommandHandler<TCommand> handler)
	{
		_handler = handler;
	}

	public async Task<Result> HandleAsync(TCommand command)
	{
		string commandJson = JsonConvert.SerializeObject(command);

		// Use proper logging here
		Console.WriteLine($"Command of type {command.GetType().Name}: {commandJson}");

		return await _handler.HandleAsync(command);
	}
}
```
```
[AuditLog]
[DatabaseRetry]
internal sealed class EditPersonalInfoCommandHandler : ICommandHandler<EditPersonalInfoCommand>
{

}
```

## Seperate Domain Models
![alt text](img/seperation.jpg "Seperation")
![alt text](img/scalability.jpg "Scalability")
* Dapper is useful for queries
* Normalized Data = Good for Commands
* Denormalized Data = Good for Queries
* Eventual consistency and maintaining a seperate database are significant costs
* Cqrs can be just as effect with only a single database.
* Sync State Driven Projection = Indexed Views
* Async State Driven Projection = Database replication
* Event Driven Projection = Only if using Event Sourcing
* Eventual Consistency = A consistency model which guarantees that, if no new updates are made to a given data item, eventually all accesses to that item will return the last updated value. 

## Cqrs vs Specification pattern
![alt text](img/specification.jpg "Specification")
* In large systems loose coupling is preferred (Cqrs)
* Reduces duplication
* lambda can either be compiled to a delegate (Func<int, bool>) or expression(Expression<Func<int, bool>>) 
* IEnumerable uses delegate.  Func<int, bool> func = x => x + 1;
* IQueryable uses expression. Expression<Func<int, bool>> expression = x => x + 1;
* Expression > Delegate. Func<int, bool> func = expression.Compile();
* Not easy to combine expressions like delegates
* GenericSpecification<T> and returning IQueryable<T> are anti-patterns.
* Best to return IReadOnlyList from repository. Better than IEnumerable as if connection is closed before enumeration occurs an error will occur.
* Use strongly typed specifications
* No need for ISpecification<T>
* Try and make the specifications encapsulate and not accept any parameters, immuntable.
* And, Or, Not combinations
* No need to use specification pattern if not using it for both Search AND Validation.

1. In Memory validation
2. Retrieve data from DB
3. Construction-to-order

```
public class Movie
{

}

public sealed class GoodMovieSpecification : Specification<Movie>
{
	public override Expression<Func<Movie, bool>> ToExpression()
	{
		return movie => movie.Rating > 7;
	}
}

public sealed class MovieForKidsSpecification : Specification<Movie>
{
	public override Expression<Func<Movie, bool>> ToExpression()
	{
		return movie => movie.ForKids == true;
	}
}
```

```
public abstract class Specification<T>
{
	//Use in repository
	//Forces Encapsulation
	public abstract Expression<Func<T, bool>> ToExpression();

	//Use in Validation
	public bool IsSatisfiedBy(T entity)
	{
		Func<T, bool> predictate = ToExpression().Compile();
		return predictate(entity);
	}

	public Specification<T> And(Specification<T> specification)
	{
		return new AndSpecification<T>(this, specification);
	}

	public Specification<T> Or(Specification<T> specification)
	{
		return new OrSpecification<T>(this, specification);
	}

	public Specification<T> Not(Specification<T> specification)
	{
		return new NotSpecification<T>(this);
	}
}


internal sealed class IdentitySpecification<T> : Specification<T>
{
	public override Expression<Func<T, bool>> ToExpression()
	{
		return x => true;
	}
}

internal sealed class AndSpecification<T> : Specification<T>
{
	private readonly Specification<T> _left;
	private readonly Specification<T> _right;

	public AndSpecification(Specification<T> left, Specification<T> right)
	{
		_left = left;
		_right = right;
	}

	public override Expression<Func<Movie, bool>> ToExpression()
	{
		Expression<Func<T, bool>> leftExpression = _left.ToExpression();
		Expression<Func<T, bool>> rightExpression = _right.ToExpression();

		BinaryExpression andExpression = Expression.AndAlso(leftExpression.Body, rightExpression.Body);

		return Expression.Lambda<Func<T, bool>>(andExpression, leftExpression.Parameters.Single());
	}
}

internal sealed class OrSpecification<T> : Specification<T>
{
	private readonly Specification<T> _left;
	private readonly Specification<T> _right;

	public OrSpecification(Specification<T> left, Specification<T> right)
	{
		_left = left;
		_right = right;
	}

	public override Expression<Func<T, bool>> ToExpression()
	{
		Expression<Func<T, bool>> leftExpression = _left.ToExpression();
		Expression<Func<T, bool>> rightExpression = _right.ToExpression();

		BinaryExpression andExpression = Expression.OrElse(leftExpression.Body, rightExpression.Body);

		return Expression.Lambda<Func<T, bool>>(andExpression, leftExpression.Parameters.Single());
	}
}

internal sealed class NotSpecification<T> : Specification<T>
{
	private readonly Specification<T> _specification;

	public NotSpecification(Specification<T> specification)
	{
		_specification = specification;
	}

	public override Expression<Func<Movie, bool>> ToExpression()
	{
		Expression<Func<T, bool>> expression = _specification.ToExpression();

		BinaryExpression notExpression = Expression.Not(expression.Body);

		return Expression.Lambda<Func<T, bool>>(notExpression, expression.Parameters.Single());
	}
}
```
```
public class MovieRepository
{
	public List<Movie> GetList(Specification<Movie> specification)
	{
		return dbContext.Mvoies.Where(specification.ToExpression()).ToList();
	}
}
```
```
var specification = new GoodMovieSpecification<Movie>();
var specification2 = new MovieForKidsSpecification<Movie>();

var spec = specification.And(specification2);

var movies = _repository.GetList(spec);

var movie = new Movie();
if(!spec.IsSatisfiedBy(movie))
{

}
```
```
var spec = Specification<Movie>.All;

if(goodMovies)
{
	spec = spec.And(new GoodMovieSpecification());
}

if(kids)
{
	spec = spec.And(new MovieForKidsSpecification());
}

var movies = _repository.GetList(spec);
```