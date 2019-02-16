using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeTrivia
{

    public static class TaskExtensions
    {

        public static Func<T> AsWaitFor<T>(this Task<T> task)
            => () =>
            {
                task.Wait();

                return task.Result;
            };

    }

}
