# GitHub Repository Setup with Visual Studio 2017

This section tells you how to build a project that is already hosted on GitHub, 
using Visual Studio 2017. If you don't already have Visual Studio 2017, you 
can get the community edition for free from 
[this link](https://www.visualstudio.com/vs/community/).
This section also goes through important routine tasks like getting the latest 
changes from GitHub and submitting a pull request to the main branch.

It will show you how to do this using just Visual Studio 2017. Older versions 
of Visual Studio as well as other IDEs are possible, but not covered here.  
You can also use 'raw' git commands but I don't cover that here.  

We use the PerfView project https://github.com/Microsoft/perfview as an example but 
the vast majority of this section applies to any open source GitHub hosted project.  

The step-by-step instructions will not make a whole lot of sense, however, without
a certain amount of background knowledge and a basic understanding of what GIT
and GitHub do along with some of the basic concepts associated with the GIT source code
control system. This is where we start.  

## Background: GitHub Repos, Commits Branches and GitHub  

The set of all files versioned as a unit by the GIT source code control system is 
called a repository, or *repo* for short. Logically, a GIT repository contains a 
collection of snapshots called *commits* which represents a set of files at one
point in time. The data in any such snapshot is hashed (including all its meta
data like when the commit was made), and this hash is the ID for the commit.
In theory the ID is very long (64+ bytes), but in practice all
GIT commands will take an unambiguous prefix. Typically 8 Hex digits are more than
sufficient so you will see instructions talking about commit 534aab38 or commit 3abcd333. 

One of the very nice properties of naming commits using a hash of the data is
that *combining two repos into a single one is trivial*.  If we assume that the
hashing GIT does is 'perfect' (and it really is close to perfect) if two commits
have the same ID (hash) then they have to have been the same data, which means
they really are the same commit.   Thus combining two repos is simply a matter of making
a union of all the commits in the two component repos.    

This ability to easily combine two repositories makes GIT well suited for 'distributed'
source code control, where there is no 'main' repository that all 'clients' work 
from. Instead each repository can be thought of as the 'main', and a repository
like GitHub is only special in the sense that it is a well known publishing point.   

Indeed, when working with GitHub you NEVER have just one repository. At the minimum
there are two repositories you need to know about. First is the repository hosted
on GitHub, and the other is a local repository on the machine where you make your 
changes. This local repository is a COMPLETE CLONE of the GitHub one (which includes
all commits and thus all history). Thus after cloning, **you create many local 
changes (commits) without every communicating with GitHub**. Only when you want
to publish (calling pushing) or get updates (called pulling) do you need connectivity to GitHub.    

### Branches

A repository is a collection of MANY snapshots (commits), and most commits have a 
predecessor (the snapshot before the change). Some commits have multiple natural 
predecessors (as when changes from multiple sources are merged). Thus there is
the concept of 'history' of a commit where you can see the set of commits, that 
over time lead to a particular place. Thus a commit can be thought of not just
as one snapshot, but in fact the complete history that is 'reachable' by following
predecessor links. GIT has a concept of a pointer to a commit (history) called
a *branch*. Most GIT commands have the concept of a 'current branch', and 
when a repo is created, typically it creates a branch called 'main' which typically
is used to represent the 'main' or maximally shared history of code (you may 
have other branches that represent releases or other specialized versions of the
code base)

While it is possible to commit your changes directly to the 'main' branch, 
branches in GIT are very cheap and GIT encourages you to make a new branch (derived
from 'main') whenever independent work is being done.   These independent branches 
can then be merged into 'main' (or used as pull requests) independently
of each other. 

## Two Possible Repository Setups

This section tells you about two possible ways to use a GitHub repository, and why
you should choose one or the other.   

### Simple GIT Repository Setup

If one of the following two conditions hold

1. You have Read/Write permission to the GitHub repository of interest.
2. You only have read access, but you never want to 'push' changes back to the repository.  

You can use a simple GIT setup with only two repositories (the one on GitHub and the local
repository on your development machine). In this setup you can pull new commits
from the GitHub repository to your local one to keep your local copy up to date.
You can even make local changes for your own personal experimentation in your local repository.
However you can only 'push' those changes back to GitHub if you have write permission to
the repo.  This is fine for personal projects, but not good enough for open source projects
because there is no way for people without write access to contribute updates.    

### Open Source Projects and Pull Requests

In an open source project, we want the ability for ANYONE to PROPOSE a change 
to the main repository, but we need an APPROVAL process so that only those with
special permissions (the maintainers) can actually update the main repository.  
GitHub accomplishes this with a process known as the *pull request*. A pull request
is a GitHub procedure where any user can indicate that he would like a particular change
(commit) integrated to the GitHub repository. People who have write permission
to the repository can look at the change and determine if it meets
the standards of the repository and if so merge (pull) the change into it. In this way
arbitrary users can contribute fixes to a repository in a controlled way.   
   
There is a logistic problem with pull requests on where to put the tentative (proposed)
changes.   We need a commit (set of file changes) that anyone can create, but can 
easily be integrated into the main GIT repository. This commit can't be created in the 
main repository (because it was created by a user without write permissions to the main repository). It also 
can't be in a user's local repository because that is on the user's machine and
not available to GitHub maintainers. Thus we need a repository that is in a public place 
(that is on GitHub), but is writeable by a particular user. GitHub solves this problem 
by making a 3rd repository which is typically called *fork*.   

Thus the typical flow for an open source project is 

 1. Create a *Fork* (copy of the repo) of the open source repository (e.g. https://github.com/Microsoft/perfview) 
 that is writable by you. There is a button on the project's GitHub page (upper right corner called 'fork') that does
 this. Every GitHub user has an 'area' assigned to their GitHub user name that can host these writable forks. There may
 be other locations (shared by a group) that you may put the fork, so when clicking the 'Fork' button it may prompt 
 you with choices of where to put it. Typically you want to use the area associated with just your user.  For example, my
 GitHub user name is vancem, so the fork created when I click on the 'fork' button of https://github.com/Microsoft/perfview creates 
 the fork called https://github.com/vancem/perfview (notice it in the 'vancem' area of GitHub which is writable by me).
 If the fork already exists, it simply takes me there (so you can find your existing forks easily).

 2. Clone your GitHub fork locally on your machine. This works just like if you had cloned the
 https://github.com/Microsoft/perfview directly, but with the significant difference that you can write 
 to this repository, so you can submit changes.

 3. Prepare a change by making a new *branch* in your *GitHub* fork with the changes you want. This actually
 involves several sub-steps 
    1. Create a new branch representing your proposed change in your local repository which is a clone of *your GitHub Fork*.
	  2. Create one or more commits in this branch that embody the change you want.  
	  3. Push your changes to your GitHub Fork.   

 4. Once you have a branch you are happy within your own fork, there is a button on YOUR FORKs GitHub page that 
 allows you to submit that branch as a pull request. When you do this you write a description / rationale for The
 change where you are persuading the maintainers to accept your request.   There is an area for discussion, and typically
 the maintainers ask for changes. You update your branch as needed, and the pull request automatically gets updated
 to the latest revision of the branch. Hopefully after enough discussion and updates, the maintainers accept
 the pull request and merge your branch from your GitHub fork into the main repository.  

 5. The next time you update your fork from the main repository, your main branch will have your pull request reflected in 
 the branch.  
   
### To Fork or not to Fork?

 So there are two different ways you can set up your clone of the repository

 1. You can simply clone the repository locally.
 2. You can create a fork, and then clone your fork locally and work with that. 

The basic answer is that (2) can do pull requests (and generally forces all updates to be pull requests), 
however it is also more cumbersome (syncing to the latest code is harder and updates have the overhead of the
pull request procedure). So the simple answer is

1. Use (1) when you have read-write access and want a low overhead checkin process (private projects typically fall into this bucket).
2. Use (1) if you have read-only access and don't need to submit fixes/changes (thus you don't need pull requests)

A typical scenario is that start with the direct (unforked) option up until the point where you want to start modifying the 
code base at which point you switch to the forked option. 

Once you have decided on an option, see one of the following.   

  * [Simple GitHub Setup and Workflow (option 1)](SimpleGitWorkflow.md)
  * [Open Source GitHub Setup and Workflow (option 2)](OpenSourceGitWorkflow.md)
