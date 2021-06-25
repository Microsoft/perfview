import React from "react";
const GroupingPatternsExampleComponent = () => {
  return (
    <div style={{ fontSize: "0.65rem" }}>
      <strong>Grouping Patterns Examples</strong>
      <table>
        <thead>
          <tr>
            <td>Pattern</td>
            <td>Comment</td>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>{`{`}%&#125;!-&gt;module $1</td>
            <td>
              <strong>Group Modules - Provides high-level overview (i.e. per dll/module cost)</strong>
            </td>
          </tr>
          <tr>
            <td>{`{`}*&#125;!=&gt;module $1</td>
            <td>Group Full Path Module Entries</td>
          </tr>
          <tr>
            <td>{`{`}%&#125;!=&gt;module $1</td>
            <td>Group Module Entries</td>
          </tr>
          <tr>
            <td>
              {`{`}%!*&#125;.%(-&gt;class $1;{`{`}%!*&#125;::-&gt;class $1
            </td>
            <td>Group Classes</td>
          </tr>
          <tr>
            <td>
              {`{`}%!*&#125;.%(=&gt;class $1;{`{`}%!*&#125;::=&gt;class $1
            </td>
            <td>Group Class Entries</td>
          </tr>
          <tr>
            <td>Thread -&gt; AllThreads</td>
            <td>Fold Threads</td>
          </tr>
        </tbody>
      </table>
    </div>
  );
};

export { GroupingPatternsExampleComponent };
